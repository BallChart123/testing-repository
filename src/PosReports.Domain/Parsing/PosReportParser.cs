using System.Globalization;
using System.Text.RegularExpressions;
using PosReports.Domain.Models;
namespace PosReports.Domain.Parsing;
public interface IPosEnvelopeValidator
{
    PosEnvelope Validate(string raw, DateTimeOffset receivedAt);
}
public interface IPosReportParser
{
    PosReport Parse(string raw, DateTimeOffset receivedAt);
}
public sealed class PosReportParser : IPosEnvelopeValidator, IPosReportParser
{
    private static readonly Regex Header = new(@"^(?<flight>[A-Z]{2}[0-9]{1,4})\.FR (?<dep>[A-Z]{3})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Destination = new(@"^TO (?<dest>[A-Z]{3})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DayTime = new(@"^\d{6}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    public PosEnvelope Validate(string raw, DateTimeOffset receivedAt)
    {
        var parts = Split(raw);
        var head = Header.Match(parts[1]);
        var dest = Destination.Match(parts[2]);
        if (!head.Success || !dest.Success || !DayTime.IsMatch(parts[3]))
            throw new InvalidPosReportException("Invalid flight header, destination, or day/time format.");
        var utc = receivedAt.ToUniversalTime();
        var date = ResolveTimestamp(parts[3], utc);
        var flight = head.Groups["flight"].Value;
        var dep = head.Groups["dep"].Value;
        var destination = dest.Groups["dest"].Value;
        var datePart = date.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var flightId = $"{flight}{datePart}{dep}{destination}";
        return new PosEnvelope(flightId, flight, dep, destination, utc);
    }
    public PosReport Parse(string raw, DateTimeOffset receivedAt)
    {
        var parts = Split(raw);
        var envelope = Validate(raw, receivedAt);
        var timestamp = ResolveTimestamp(parts[3], receivedAt.ToUniversalTime());
        var (lat, lon) = CoordinateConverter.Parse(parts[4]);
        var speed = ParseNumber(parts[5], "ground speed", strictlyPositive: true);
        var fuel = ParseNumber(parts[6], "fuel on board");
        var flow = ParseNumber(parts[7], "fuel flow");
        return new PosReport(envelope.FlightId, envelope.FlightNumber, envelope.Departure, envelope.Destination,
            timestamp, lat, lon, speed, fuel, flow);
    }
    private static string[] Split(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) throw new InvalidPosReportException("POS report is empty.");
        var parts = raw.Trim().Split('/');
        if (parts.Length != 8 || parts[0] != "POS")
            throw new InvalidPosReportException("Expected POS/ followed by seven fields.");
        return parts;
    }
    private static DateTimeOffset ResolveTimestamp(string dayTime, DateTimeOffset utc)
    {
        if (!DayTime.IsMatch(dayTime)) throw new InvalidPosReportException("Invalid day/time format.");
        var day = int.Parse(dayTime[..2], CultureInfo.InvariantCulture);
        var hour = int.Parse(dayTime.Substring(2, 2), CultureInfo.InvariantCulture);
        var minute = int.Parse(dayTime.Substring(4, 2), CultureInfo.InvariantCulture);
        if (day < 1 || day > DateTime.DaysInMonth(utc.Year, utc.Month) || hour > 23 || minute > 59)
            throw new InvalidPosReportException("Invalid report day or time for reception month.");
        return new DateTimeOffset(utc.Year, utc.Month, day, hour, minute, 0, TimeSpan.Zero);
    }
    private static double ParseNumber(string value, string field, bool strictlyPositive = false)
    {
        if (!double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
            || !double.IsFinite(parsed) || parsed < 0 || (strictlyPositive && parsed == 0))
            throw new InvalidPosReportException($"Invalid {field}.");
        return parsed;
    }
}
