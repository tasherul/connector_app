using System.Text.RegularExpressions;

namespace RetwhoConnector.Core.Models;

public enum AgentLogLevel
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Critical = 5,
}

public enum AgentLogCategory
{
    General = 0,
    Success = 1,
    Session = 2,
    Action = 3,
    Error = 4,
}

public enum LoggingHealthState
{
    Healthy = 0,
    Degraded = 1,
    Stopped = 2,
}

public sealed record LogEntry
{
    private static readonly Regex CorrelationIdPattern = new(
        "^[A-Za-z0-9._:~-]{1,128}$",
        RegexOptions.CultureInvariant);

    public LogEntry(
        DateTimeOffset timestampUtc,
        AgentLogLevel level,
        AgentLogCategory category,
        string message,
        string? details = null,
        string? correlationId = null)
    {
        if (timestampUtc == default)
        {
            throw new ArgumentException(
                "The log timestamp is required.",
                nameof(timestampUtc));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (correlationId is not null &&
            !CorrelationIdPattern.IsMatch(correlationId))
        {
            throw new ArgumentException(
                "The log correlation ID has an invalid format.",
                nameof(correlationId));
        }

        TimestampUtc = timestampUtc.ToUniversalTime();
        Level = level;
        Category = category;
        Message = message;
        Details = details;
        CorrelationId = correlationId;
    }

    public DateTimeOffset TimestampUtc { get; }
    public AgentLogLevel Level { get; }
    public AgentLogCategory Category { get; }
    public string Message { get; }
    public string? Details { get; }
    public string? CorrelationId { get; }
}

public sealed record LogPipelineHealth
{
    public LogPipelineHealth(
        LoggingHealthState state,
        long droppedEntries,
        string message)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(droppedEntries);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        State = state;
        DroppedEntries = droppedEntries;
        Message = message;
    }

    public LoggingHealthState State { get; }
    public long DroppedEntries { get; }
    public string Message { get; }
}
