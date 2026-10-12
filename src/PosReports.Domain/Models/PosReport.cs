namespace PosReports.Domain.Models;
public sealed record PosReport(string FlightId, string FlightNumber, string Departure, string Destination, DateTimeOffset Timestamp, double Latitude, double Longitude, double GroundSpeedKnots, double FuelOnBoardKg, double FuelFlowKgPerHour);
