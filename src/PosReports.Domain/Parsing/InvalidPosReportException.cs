namespace PosReports.Domain.Parsing;
public sealed class InvalidPosReportException : Exception
{
    public InvalidPosReportException(string message) : base(message) { }
}
