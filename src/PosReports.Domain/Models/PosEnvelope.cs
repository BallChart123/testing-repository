namespace PosReports.Domain.Models;
public sealed record PosEnvelope(string FlightId, string FlightNumber, string Departure, string Destination, DateTimeOffset ReceivedAt);
