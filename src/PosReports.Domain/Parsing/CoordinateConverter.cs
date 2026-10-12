using System.Globalization;
using System.Text.RegularExpressions;
namespace PosReports.Domain.Parsing;
public static class CoordinateConverter
{
    private static readonly Regex Pattern = new(@"^(?<latHem>[NS])(?<latDeg>\d{2})(?<latMin>\d{2}(?:\.\d+)?)(?<lonHem>[EW])(?<lonDeg>\d{3})(?<lonMin>\d{2}(?:\.\d+)?)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    public static (double Latitude, double Longitude) Parse(string coordinate)
    {
        var match = Pattern.Match(coordinate ?? string.Empty);
        if (!match.Success) throw new InvalidPosReportException("Invalid coordinate format.");
        var latitude = ConvertPart(match, "latDeg", "latMin", "latHem", 90);
        var longitude = ConvertPart(match, "lonDeg", "lonMin", "lonHem", 180);
        return (latitude, longitude);
    }
    private static double ConvertPart(Match match, string deg, string min, string hem, int max)
    {
        var degrees = int.Parse(match.Groups[deg].Value, CultureInfo.InvariantCulture);
        var minutes = double.Parse(match.Groups[min].Value, CultureInfo.InvariantCulture);
        if (degrees > max || minutes >= 60 || (degrees == max && minutes > 0))
            throw new InvalidPosReportException("Coordinate out of range.");
        var value = degrees + minutes / 60.0;
        return match.Groups[hem].Value is "S" or "W" ? -value : value;
    }
}
