using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using PosReports.Domain.Models;
using PosReports.Domain.Parsing;
using PosReports.Domain.Calculations;
using PosReports.Domain.Airports;

namespace PosReports.Application;

public sealed record ParsedRecord(string FlightId, string Timestamp, string Attachment, string Fingerprint);
public sealed record ResultRecord(string FlightId, string Timestamp, string Attachment, string SourceReportTimestamp);
public sealed record CalculationMessage(string FlightId, string Timestamp);
public sealed record Receipt(string FlightId, string Status);
public sealed record CalculationOutput(string FlightId, string Timestamp, int RemainingFlightTimeMinutes, double EstimatedFuelAtArrivalKg, bool LowFuelWarning);
public sealed record StatusPending(string FlightId, string Status);
public sealed class PipelineConflictException(string message) : Exception(message);
public sealed class MissingDependencyException(string message) : Exception(message);
public interface IClock { DateTimeOffset UtcNow { get; } }
public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
public interface IObjectStore {
    Task<bool> PutIfAbsentAsync(string key, string text, string contentType, CancellationToken ct);
    Task<string> ReadAsync(string key, CancellationToken ct);
}
public interface IParsedRepository {
    Task<ParsedRecord?> GetAsync(string flightId, string timestamp, CancellationToken ct);
    Task<ParsedRecord?> LatestAsync(string flightId, CancellationToken ct);
    Task<bool> PutIfAbsentAsync(ParsedRecord record, CancellationToken ct);
}
public interface IResultRepository {
    Task<ResultRecord?> LatestAsync(string flightId, CancellationToken ct);
    Task<bool> PutIfAbsentAsync(ResultRecord record, CancellationToken ct);
    Task<bool> HasSourceAsync(string flightId, string sourceTimestamp, CancellationToken ct);
}
public interface IMessagePublisher { Task PublishAsync(CalculationMessage message, CancellationToken ct); }
public static class PosJson {
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) {
        PropertyNameCaseInsensitive = true,
        Converters = { new UtcTimestampConverter() }
    };
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options) ?? throw new FormatException("JSON is null");
}
public sealed class UtcTimestampConverter : JsonConverter<DateTimeOffset> {
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToUniversalTime();
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
}
public static class Keys {
    public static string Time(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    public static string Raw(string flightId, DateTimeOffset now) => $"pos/{flightId}-{now.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)}";
    public static string Attachment(string flightId, string timestamp) => $"attachment/{flightId}-{timestamp}.json";
    public static string Result(string flightId, DateTimeOffset now) => $"results/{flightId}-{now.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture)}.json";
    public static DateTimeOffset RawReceivedAt(string key) {
        var match = Regex.Match(key, @"^pos/[A-Z0-9]+-(?<date>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z)$");
        if (!match.Success) throw new FormatException("Invalid raw object key");
        return DateTimeOffset.ParseExact(match.Groups["date"].Value, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
    }
    public static string Fingerprint(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
public sealed class ReceivePosReport(IPosEnvelopeValidator validator, IObjectStore store, IClock clock) {
    public async Task<Receipt> ExecuteAsync(string raw, CancellationToken ct) {
        var now = clock.UtcNow;
        var envelope = validator.Validate(raw, now);
        var key = Keys.Raw(envelope.FlightId, now);
        // Retry with same raw object key is safe; concurrent distinct requests get distinct timestamps.
        if (!await store.PutIfAbsentAsync(key, raw, "text/plain", ct))
            throw new PipelineConflictException("Raw object key already exists; retry request");
        return new Receipt(envelope.FlightId, "RECEIVED");
    }
}
public sealed class ProcessPosReport(IObjectStore store, IPosReportParser parser, IParsedRepository repository, IMessagePublisher publisher) {
    public async Task ExecuteAsync(string key, CancellationToken ct) {
        if (!key.StartsWith("pos/", StringComparison.Ordinal)) throw new FormatException("Unexpected S3 prefix");
        var receivedAt = Keys.RawReceivedAt(key);
        var raw = await store.ReadAsync(key, ct);
        var report = parser.Parse(raw, receivedAt);
        if (!key.StartsWith($"pos/{report.FlightId}-", StringComparison.Ordinal)) throw new PipelineConflictException("Raw key flight identity mismatch");
        var timestamp = Keys.Time(report.Timestamp);
        var content = PosJson.Serialize(report);
        var fingerprint = Keys.Fingerprint(content);
        var attachment = Keys.Attachment(report.FlightId, timestamp);
        var existing = await repository.GetAsync(report.FlightId, timestamp, ct);
        if (existing is not null && existing.Fingerprint != fingerprint) throw new PipelineConflictException("Conflicting report for same flight and minute");
        if (!await store.PutIfAbsentAsync(attachment, content, "application/json", ct)) {
            var oldContent = await store.ReadAsync(attachment, ct);
            if (Keys.Fingerprint(oldContent) != fingerprint) throw new PipelineConflictException("Attachment collision");
        }
        var record = new ParsedRecord(report.FlightId, timestamp, attachment, fingerprint);
        if (!await repository.PutIfAbsentAsync(record, ct)) {
            existing = await repository.GetAsync(report.FlightId, timestamp, ct);
            if (existing is null || existing.Attachment != attachment ||
                (existing.Fingerprint != fingerprint && (existing.Fingerprint != "" || Keys.Fingerprint(await store.ReadAsync(existing.Attachment, ct)) != fingerprint)))
                throw new PipelineConflictException("Parsed metadata conflict");
        }
        // Always publish: a prior invocation might have saved metadata but failed before SQS SendMessage.
        await publisher.PublishAsync(new CalculationMessage(report.FlightId, timestamp), ct);
    }
}
public sealed class CalculateFlight(IObjectStore store, IParsedRepository parsed, IResultRepository results, IAirportCatalog airports, IFlightCalculator calculator, IClock clock) {
    public async Task ExecuteAsync(CalculationMessage message, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(message.FlightId) || string.IsNullOrWhiteSpace(message.Timestamp)) throw new FormatException("Invalid SQS message");
        if (await results.HasSourceAsync(message.FlightId, message.Timestamp, ct)) return;
        var metadata = await parsed.GetAsync(message.FlightId, message.Timestamp, ct) ?? throw new MissingDependencyException("Parsed record not found");
        var report = PosJson.Deserialize<PosReport>(await store.ReadAsync(metadata.Attachment, ct));
        if (report.FlightId != message.FlightId || Keys.Time(report.Timestamp) != message.Timestamp) throw new PipelineConflictException("Parsed content mismatch");
        var calculatedAt = clock.UtcNow;
        var calculation = calculator.Calculate(report, airports.Find(report.Destination), calculatedAt);
        var calculationTimestamp = calculatedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
        var output = new CalculationOutput(report.FlightId, calculationTimestamp, calculation.RemainingFlightTimeMinutes, calculation.EstimatedFuelAtArrivalKg, calculation.LowFuelWarning);
        var key = Keys.Result(report.FlightId, calculatedAt);
        if (!await store.PutIfAbsentAsync(key, PosJson.Serialize(output), "application/json", ct)) throw new PipelineConflictException("Result key collision");
        var record = new ResultRecord(report.FlightId, calculationTimestamp, key, message.Timestamp);
        if (!await results.PutIfAbsentAsync(record, ct))
            throw new PipelineConflictException("Calculation result key collision");
        // Best effort dedupe: two concurrent SQS deliveries can still write separate results.
    }
}
public sealed class GetFlightStatus(IParsedRepository parsed, IResultRepository results, IObjectStore store) {
    public async Task<object?> ExecuteAsync(string flightId, CancellationToken ct) {
        if (!Regex.IsMatch(flightId, @"^[A-Z]{2}\d{1,4}\d{8}[A-Z]{6}$")) throw new FormatException("Invalid flight ID");
        var result = await results.LatestAsync(flightId, ct);
        if (result is not null) return PosJson.Deserialize<CalculationOutput>(await store.ReadAsync(result.Attachment, ct));
        return await parsed.LatestAsync(flightId, ct) is not null ? new StatusPending(flightId, "PROCESSING") : null;
    }
}
