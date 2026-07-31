using System.Text.Json;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Models;
using RetwhoConnector.Core.Serialization;

namespace RetwhoConnector.Tests;

public sealed class LoggingContractTests
{
    [Fact]
    public void LogEntry_SerializesWithStableCamelCaseContract()
    {
        var entry = new LogEntry(
            DateTimeOffset.Parse("2026-07-31T08:20:01.250Z"),
            AgentLogLevel.Information,
            AgentLogCategory.Success,
            "POS request completed.",
            details: null,
            correlationId: "action-1");

        string json = JsonSerializer.Serialize(entry, ConnectorJson.Options);

        Assert.Equal(
            """{"timestampUtc":"2026-07-31T08:20:01.25+00:00","level":2,"category":1,"message":"POS request completed.","correlationId":"action-1"}""",
            json);
    }

    [Theory]
    [InlineData("")]
    [InlineData("contains space")]
    [InlineData("bad/slash")]
    public void LogEntry_RejectsUnsafeCorrelationIds(string correlationId)
    {
        Assert.Throws<ArgumentException>(
            () => new LogEntry(
                DateTimeOffset.UtcNow,
                AgentLogLevel.Information,
                AgentLogCategory.Action,
                "Action received.",
                details: null,
                correlationId));
    }

    [Fact]
    public void LogEntry_RejectsOverlongCorrelationId()
    {
        Assert.Throws<ArgumentException>(
            () => new LogEntry(
                DateTimeOffset.UtcNow,
                AgentLogLevel.Information,
                AgentLogCategory.Action,
                "Action received.",
                details: null,
                new string('a', 129)));
    }

    [Fact]
    public void LoggingHealth_ReportsDroppedEntries()
    {
        var health = new LogPipelineHealth(
            LoggingHealthState.Degraded,
            droppedEntries: 3,
            "The UI log queue dropped entries.");

        Assert.Equal(LoggingHealthState.Degraded, health.State);
        Assert.Equal(3, health.DroppedEntries);
        Assert.Equal(
            "The UI log queue dropped entries.",
            health.Message);
    }

    [Fact]
    public void AgentLogContract_AcceptsStructuredSafeEntry()
    {
        IAgentLog log = new RecordingAgentLog();

        bool accepted = log.TryWrite(
            AgentLogLevel.Warning,
            AgentLogCategory.Session,
            "POS session refresh started.",
            correlationId: "action-2");

        Assert.True(accepted);
    }

    private sealed class RecordingAgentLog : IAgentLog
    {
        public LogPipelineHealth CurrentHealth { get; } =
            new(LoggingHealthState.Healthy, 0, "Logging is healthy.");

        public event EventHandler<LogPipelineHealth>? HealthChanged
        {
            add { }
            remove { }
        }

        public bool TryWrite(
            AgentLogLevel level,
            AgentLogCategory category,
            string message,
            string? details = null,
            string? correlationId = null) => true;
    }
}
