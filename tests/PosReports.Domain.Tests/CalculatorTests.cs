using PosReports.Domain.Airports;
using PosReports.Domain.Calculations;
using PosReports.Domain.Parsing;
using Xunit;
namespace PosReports.Domain.Tests;
public sealed class CalculatorTests
{
    private readonly PosReportParser _parser = new();
    private readonly FlightCalculator _calculator = new();
    private readonly InMemoryAirportCatalog _airports = new();
    private const string Sample = "POS/UL204.FR RGN/TO BKK/041205/N1642.3E09612.5/450/12500/2800";
    [Fact] public void ComputesHaversineAndFuel()
    {
        var report = _parser.Parse(Sample, new DateTimeOffset(2026, 9, 4, 15, 0, 0, TimeSpan.Zero));
        var result = _calculator.Calculate(report, _airports.Find("BKK"), new DateTimeOffset(2026, 9, 4, 15, 1, 0, TimeSpan.Zero));
        Assert.InRange(result.RemainingFlightTimeMinutes, 40, 45);
        Assert.InRange(result.EstimatedFuelAtArrivalKg, 10400, 10600);
        Assert.False(result.LowFuelWarning);
        Assert.Equal(report.FlightId, result.FlightId);
    }
    [Fact] public void WarnsOnNegativeArrivalFuel()
    {
        var report = _parser.Parse(Sample.Replace("/12500/", "/100/"), new DateTimeOffset(2026, 9, 4, 15, 0, 0, TimeSpan.Zero));
        var result = _calculator.Calculate(report, _airports.Find("BKK"), DateTimeOffset.UtcNow);
        Assert.True(result.LowFuelWarning);
        Assert.True(result.EstimatedFuelAtArrivalKg < 0);
    }
    [Fact] public void SamePointIsZeroDistance() => Assert.Equal(0, FlightCalculator.HaversineDistanceNm(13.69, 100.75, 13.69, 100.75), 8);
    [Fact] public void RejectsUnknownAirport() => Assert.Throws<UnsupportedAirportException>(() => _airports.Find("XXX"));
    [Fact] public void RejectsZeroSpeed() => Assert.Throws<InvalidPosReportException>(() => _parser.Parse(Sample.Replace("/450/", "/0/"), new DateTimeOffset(2026, 9, 4, 15, 0, 0, TimeSpan.Zero)));
}
