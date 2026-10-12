using System.Globalization;
using PosReports.Domain.Parsing;
using Xunit;
namespace PosReports.Domain.Tests;
public sealed class ParserTests
{
    private readonly PosReportParser _parser = new();
    private static readonly DateTimeOffset Received = new(2026, 9, 4, 15, 12, 30, TimeSpan.Zero);
    private const string Sample = "POS/UL204.FR RGN/TO BKK/041205/N1642.3E09612.5/450/12500/2800";
    [Fact] public void ParsesAssessmentExample()
    {
        var p = _parser.Parse(Sample, Received);
        Assert.Equal("UL20420260904RGNBKK", p.FlightId);
        Assert.Equal(new DateTimeOffset(2026, 9, 4, 12, 5, 0, TimeSpan.Zero), p.Timestamp);
        Assert.Equal(16.705, p.Latitude, 6);
        Assert.Equal(96.208333333, p.Longitude, 6);
        Assert.Equal(450, p.GroundSpeedKnots);
        Assert.Equal(12500, p.FuelOnBoardKg);
        Assert.Equal(2800, p.FuelFlowKgPerHour);
    }
    [Theory]
    [InlineData("S1642.3W09612.5", -16.705, -96.208333333)]
    [InlineData("N1642.3E09612.5", 16.705, 96.208333333)]
    public void ConvertsHemisphere(string raw, double lat, double lon)
    {
        var actual = CoordinateConverter.Parse(raw);
        Assert.Equal(lat, actual.Latitude, 6);
        Assert.Equal(lon, actual.Longitude, 6);
    }
    [Theory]
    [InlineData("N1660.0E09612.5")]
    [InlineData("N9100.0E09612.5")]
    [InlineData("N1642.3E18100.0")]
    [InlineData("N1642.3E09660.0")]
    public void RejectsInvalidCoordinates(string raw) => Assert.Throws<InvalidPosReportException>(() => CoordinateConverter.Parse(raw));
    [Theory]
    [InlineData("BAD/UL204.FR RGN/TO BKK/041205/N1642.3E09612.5/450/12500/2800")]
    [InlineData("POS/UL204.FR RGN/TO BKK/041205/N1642.3E09612.5/0/12500/2800")]
    [InlineData("POS/UL204.FR RGN/TO BKK/041205/N1642.3E09612.5/450/-1/2800")]
    [InlineData("POS/UL204.FR RGN/TO BKK/041205/N1642.3E09612.5/450/12500")]
    [InlineData("POS/UL204.FR RGN/TO BKK/041205/N1642.3E09612.5/450/NaN/2800")]
    public void RejectsInvalidReports(string raw) => Assert.Throws<InvalidPosReportException>(() => _parser.Parse(raw, Received));
    [Fact] public void RejectsInvalidCalendarDay() => Assert.Throws<InvalidPosReportException>(() => _parser.Parse(Sample.Replace("041205", "311205"), Received));
    [Fact] public void SupportsLeapDay() => Assert.Equal("UL20420240229RGNBKK", _parser.Parse(Sample.Replace("041205", "291205"), new DateTimeOffset(2024, 2, 10, 0, 0, 0, TimeSpan.Zero)).FlightId);
    [Fact] public void FlightIdUsesGregorianYearUnderThaiCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
            Assert.Equal("UL20420260904RGNBKK", _parser.Parse(Sample, Received).FlightId);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }
    [Fact] public void UsesReceptionMonthRatherThanProcessingMonth() => Assert.Equal("UL20420260904RGNBKK", _parser.Parse(Sample, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(-1)).FlightId);
}
