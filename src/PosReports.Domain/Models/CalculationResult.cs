namespace PosReports.Domain.Models;
public sealed record CalculationInput(double CurrentLatitude, double CurrentLongitude, string Destination, double GroundSpeedKnots, double FuelOnBoardKg, double FuelFlowKgPerHour);
public sealed record CalculationResult(string FlightId, DateTimeOffset CalculatedAt, CalculationInput Input, int RemainingFlightTimeMinutes, double EstimatedFuelAtArrivalKg, bool LowFuelWarning);
