using System.Globalization;
using PosReports.Application;
using PosReports.Domain.Parsing;
using PosReports.Domain.Calculations;
using PosReports.Domain.Airports;
using Xunit;
namespace PosReports.Application.Tests;
public sealed class PipelineTests {
    const string Raw="POS/UL204.FR RGN/TO BKK/041205/N1642.3E09612.5/450/12500/2800";
    static readonly DateTimeOffset Now=new(2026,9,4,15,12,30,TimeSpan.Zero);
    sealed class Clock : IClock { public DateTimeOffset UtcNow => Now; }
    sealed class Store : IObjectStore {
        public readonly Dictionary<string,string> Objects=new();
        public Task<bool> PutIfAbsentAsync(string key,string text,string type,CancellationToken ct)=>Task.FromResult(Objects.TryAdd(key,text));
        public Task<string> ReadAsync(string key,CancellationToken ct)=>Task.FromResult(Objects[key]);
    }
    sealed class Parsed : IParsedRepository {
        public readonly Dictionary<string,ParsedRecord> Records=new();
        public Task<ParsedRecord?> GetAsync(string id,string ts,CancellationToken ct)=>Task.FromResult(Records.GetValueOrDefault(id+ts));
        public Task<ParsedRecord?> LatestAsync(string id,CancellationToken ct)=>Task.FromResult(Records.Values.Where(x=>x.FlightId==id).OrderByDescending(x=>x.Timestamp).FirstOrDefault());
        public Task<bool> PutIfAbsentAsync(ParsedRecord r,CancellationToken ct)=>Task.FromResult(Records.TryAdd(r.FlightId+r.Timestamp,r));
    }
    sealed class Results : IResultRepository {
        public readonly List<ResultRecord> Records=new();
        public Task<ResultRecord?> LatestAsync(string id,CancellationToken ct)=>Task.FromResult(Records.Where(x=>x.FlightId==id).OrderByDescending(x=>x.Timestamp).FirstOrDefault());
        public Task<bool> PutIfAbsentAsync(ResultRecord r,CancellationToken ct){Records.Add(r);return Task.FromResult(true);}
        public Task<bool> HasSourceAsync(string id,string ts,CancellationToken ct)=>Task.FromResult(Records.Any(x=>x.FlightId==id&&x.SourceReportTimestamp==ts));
    }
    sealed class Publisher : IMessagePublisher {
        public readonly List<CalculationMessage> Messages=new();
        public Task PublishAsync(CalculationMessage msg,CancellationToken ct){Messages.Add(msg);return Task.CompletedTask;}
    }
    [Fact] public async Task EndToEnd_InMemory_And_DuplicateDelivery() {
        var store=new Store();var parsed=new Parsed();var results=new Results();var pub=new Publisher();var parser=new PosReportParser();
        var receipt=await new ReceivePosReport(parser,store,new Clock()).ExecuteAsync(Raw,default);
        Assert.Equal("UL20420260904RGNBKK",receipt.FlightId);
        var rawKey=Assert.Single(store.Objects.Keys);
        var process=new ProcessPosReport(store,parser,parsed,pub);
        await process.ExecuteAsync(rawKey,default);
        await process.ExecuteAsync(rawKey,default);
        Assert.Single(parsed.Records);Assert.Equal(2,pub.Messages.Count);
        var calculate=new CalculateFlight(store,parsed,results,new InMemoryAirportCatalog(),new FlightCalculator(),new Clock());
        await calculate.ExecuteAsync(pub.Messages[0],default);
        await calculate.ExecuteAsync(pub.Messages[1],default);
        Assert.Single(results.Records);
        var status=await new GetFlightStatus(parsed,results,store).ExecuteAsync(receipt.FlightId,default);
        Assert.IsType<CalculationOutput>(status);
    }
    [Fact] public void RawAndResultKeysUseGregorianYearUnderThaiCulture() {
        var original = CultureInfo.CurrentCulture;
        try {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
            var raw = Keys.Raw("UL20420260904RGNBKK", Now);
            Assert.Contains("2026-09-04", raw);
            Assert.Equal(Now, Keys.RawReceivedAt(raw));
            Assert.Contains("2026-09-04", Keys.Result("UL20420260904RGNBKK", Now));
        } finally { CultureInfo.CurrentCulture = original; }
    }
    [Fact] public void DateFromRawKey_PreservesReceptionMonth() {
        var key=Keys.Raw("UL20420260904RGNBKK",Now);
        Assert.Equal(Now,Keys.RawReceivedAt(key));
    }
}
