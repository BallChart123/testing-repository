using PosReports.Domain.Models;
namespace PosReports.Domain.Airports;
public interface IAirportCatalog
{
    AirportCoordinates Find(string airportCode);
}
public sealed class UnsupportedAirportException : Exception
{
    public UnsupportedAirportException(string code) : base($"Unsupported airport: {code}") { }
}
public sealed class InMemoryAirportCatalog : IAirportCatalog
{
    private static readonly IReadOnlyDictionary<string, AirportCoordinates> Airports =
        new Dictionary<string, AirportCoordinates>(StringComparer.Ordinal)
        {
            ["RGN"] = new("RGN", 16.9073, 96.1332),
            ["BKK"] = new("BKK", 13.6900, 100.7501),
            ["SIN"] = new("SIN", 1.3644, 103.9915)
        };
    public AirportCoordinates Find(string airportCode) =>
        Airports.TryGetValue(airportCode ?? string.Empty, out var airport)
            ? airport : throw new UnsupportedAirportException(airportCode ?? "<null>");
}
