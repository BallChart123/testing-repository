using Amazon.Lambda.Core;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.S3Events;
using Amazon.Lambda.SQSEvents;
using Amazon.Lambda.Serialization.SystemTextJson;
using Amazon.S3;
using Amazon.DynamoDBv2;
using Amazon.SQS;
using PosReports.Application;
using PosReports.Infrastructure;
using PosReports.Domain.Parsing;
using PosReports.Domain.Calculations;
using PosReports.Domain.Airports;
using System.Text;
using System.Text.Json;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]
namespace PosReports.Functions;
public static class Dependencies {
    private static readonly IAmazonS3 S3 = new AmazonS3Client();
    private static readonly IAmazonDynamoDB Db = new AmazonDynamoDBClient();
    private static readonly IAmazonSQS Sqs = new AmazonSQSClient();
    public static readonly IClock Clock = new SystemClock();
    public static readonly PosReportParser Parser = new();
    public static IObjectStore Store => new S3ObjectStore(S3, Env("BUCKET_NAME"));
    public static IParsedRepository Parsed => new DynamoParsedRepository(Db, Env("PARSED_TABLE_NAME"));
    public static IResultRepository Results => new DynamoResultRepository(Db, Env("RESULT_TABLE_NAME"));
    public static IMessagePublisher Publisher => new SqsPublisher(Sqs, Env("CALCULATION_QUEUE_URL"));
    public static string Env(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException($"Missing {name}");
}
public static class Http {
    public static APIGatewayProxyResponse Reply(int status,object value) => new() {StatusCode=status,Headers=new Dictionary<string,string>{{"Content-Type","application/json"}},Body=PosJson.Serialize(value)};
}
public sealed class ReceiverFunction {
    public async Task<APIGatewayProxyResponse> Handler(APIGatewayProxyRequest request,ILambdaContext context) {
        try {
            var raw=request.Body ?? "";
            if(request.IsBase64Encoded) raw=Encoding.UTF8.GetString(Convert.FromBase64String(raw));
            var receipt=await new ReceivePosReport(Dependencies.Parser,Dependencies.Store,Dependencies.Clock).ExecuteAsync(raw,default);
            context.Logger.LogLine("event=pos_received status=RECEIVED");
            return Http.Reply(202,receipt);
        } catch(InvalidPosReportException e) {return Http.Reply(400,new{code="INVALID_POS_REPORT",message=e.Message});}
          catch(FormatException) {return Http.Reply(400,new{code="INVALID_POS_REPORT",message="Invalid body encoding"});}
          catch(PipelineConflictException) {return Http.Reply(409,new{code="CONFLICT",message="Raw report key conflict; retry"});}
          catch(Exception e) {context.Logger.LogLine($"event=receiver_failed errorType={e.GetType().Name}");return Http.Reply(500,new{code="INTERNAL_SERVER_ERROR",message="Unable to receive POS report"});}
    }
}
public sealed class ParserFunction {
    public async Task Handler(S3Event evt,ILambdaContext context) {
        var useCase=new ProcessPosReport(Dependencies.Store,Dependencies.Parser,Dependencies.Parsed,Dependencies.Publisher);
        foreach(var record in evt.Records) {
            var key=Uri.UnescapeDataString(record.S3.Object.Key.Replace('+',' '));
            try { await useCase.ExecuteAsync(key,default); context.Logger.LogLine("event=pos_parsed status=COMPLETED"); }
            catch(Exception e) {context.Logger.LogLine($"event=parser_failed errorType={e.GetType().Name}");throw;}
        }
    }
}
public sealed class CalculatorFunction {
    public async Task Handler(SQSEvent evt,ILambdaContext context) {
        var useCase=new CalculateFlight(Dependencies.Store,Dependencies.Parsed,Dependencies.Results,new InMemoryAirportCatalog(),new FlightCalculator(),Dependencies.Clock);
        foreach(var record in evt.Records) {
            try { var msg=PosJson.Deserialize<CalculationMessage>(record.Body);await useCase.ExecuteAsync(msg,default);context.Logger.LogLine("event=calculation_completed status=COMPLETED"); }
            catch(Exception e) {context.Logger.LogLine($"event=calculator_failed errorType={e.GetType().Name}");throw;}
        }
    }
}
public sealed class StatusFunction {
    public async Task<APIGatewayProxyResponse> Handler(APIGatewayProxyRequest request,ILambdaContext context) {
        try {
            var id=request.PathParameters is not null && request.PathParameters.TryGetValue("flightId",out var v)?v:"";
            var value=await new GetFlightStatus(Dependencies.Parsed,Dependencies.Results,Dependencies.Store).ExecuteAsync(id,default);
            return value is null?Http.Reply(404,new{code="FLIGHT_NOT_FOUND",message="No report found"}):Http.Reply(200,value);
        } catch(FormatException) {return Http.Reply(400,new{code="INVALID_FLIGHT_ID",message="Invalid flight ID"});}
          catch(Exception e) {context.Logger.LogLine($"event=status_failed errorType={e.GetType().Name}");return Http.Reply(500,new{code="INTERNAL_SERVER_ERROR",message="Unable to read status"});}
    }
}
