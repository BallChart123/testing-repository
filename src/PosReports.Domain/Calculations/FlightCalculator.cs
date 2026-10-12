using PosReports.Domain.Models;
namespace PosReports.Domain.Calculations;
public interface IFlightCalculator
{
    CalculationResult Calculate(PosReport report, AirportCoordinates destination, DateTimeOffset calculatedAt);
}
public sealed class FlightCalculator : IFlightCalculator
{
    public const double EarthRadiusNauticalMiles = 3440.065;
    public CalculationResult Calculate(PosReport report, AirportCoordinates destination, DateTimeOffset calculatedAt)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(destination);
        if (report.GroundSpeedKnots <= 0 || !double.IsFinite(report.GroundSpeedKnots))
            throw new ArgumentOutOfRangeException(nameof(report), "Ground speed must be positive and finite.");
        if (report.FuelOnBoardKg < 0 || report.FuelFlowKgPerHour < 0 ||
            !double.IsFinite(report.FuelOnBoardKg) || !double.IsFinite(report.FuelFlowKgPerHour))
            throw new ArgumentOutOfRangeException(nameof(report), "Fuel quantities must be non-negative and finite.");
        ValidateCoordinates(report.Latitude, report.Longitude);
        ValidateCoordinates(destination.Latitude, destination.Longitude);
        if (!string.Equals(report.Destination, destination.Code, StringComparison.Ordinal))
            throw new ArgumentException("Destination airport does not match report.", nameof(destination));
        var distance = HaversineDistanceNm(report.Latitude, report.Longitude, destination.Latitude, destination.Longitude);
        var hours = distance / report.GroundSpeedKnots;
        var minutes = checked((int)Math.Round(hours * 60, 0, MidpointRounding.AwayFromZero));
        var fuel = report.FuelOnBoardKg - report.FuelFlowKgPerHour * hours;
        return new CalculationResult(report.FlightId, calculatedAt.ToUniversalTime(),
            new CalculationInput(report.Latitude, report.Longitude, report.Destination, report.GroundSpeedKnots,
                report.FuelOnBoardKg, report.FuelFlowKgPerHour), minutes, fuel, fuel < 0);
    }
    public static double HaversineDistanceNm(double lat1, double lon1, double lat2, double lon2)
    {
        ValidateCoordinates(lat1, lon1);
        ValidateCoordinates(lat2, lon2);
        static double Rad(double degrees) => degrees * Math.PI / 180.0;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Pow(Math.Sin(dLon / 2), 2);
        var bounded = Math.Clamp(a, 0, 1);
        return EarthRadiusNauticalMiles * 2 * Math.Atan2(Math.Sqrt(bounded), Math.Sqrt(1 - bounded));
    }
    private static void ValidateCoordinates(double lat, double lon)
    {
        if (!double.IsFinite(lat) || !double.IsFinite(lon) || Math.Abs(lat) > 90 || Math.Abs(lon) > 180)
            throw new ArgumentOutOfRangeException(nameof(lat), "Invalid coordinates.");
    }
}
