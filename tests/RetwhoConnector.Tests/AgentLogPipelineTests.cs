using Microsoft.Extensions.Logging;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Configuration;
using RetwhoConnector.Core.Models;
using RetwhoConnector.Core.Security;
using RetwhoConnector.Core.Services;

namespace RetwhoConnector.Tests;

public sealed class AgentLogPipelineTests
{
    [Fact]
    public async Task Pipeline_SanitizesAndBoundsContentBeforeSink()
    {
        var sink = new RecordingLogSink();
        var pipeline = CreatePipeline(sink);
        await pipeline.StartAsync(CancellationToken.None);

        Assert.True(pipeline.TryWrite(
            AgentLogLevel.Warning,
            AgentLogCategory.Session,
            "password=FAKE_PASSWORD " + new string('m', 5_000),
            "cookie=FAKE_COOKIE " + new string('d', 40_000),
            "action-1"));
        await pipeline.StopAsync(CancellationToken.None);

        LogEntry entry = Assert.Single(sink.Entries);
        Assert.Equal(4_096, entry.Message.Length);
        Assert.Equal(32_768, entry.Details?.Length);
        Assert.EndsWith("…", entry.Message, StringComparison.Ordinal);
        Assert.EndsWith("…", entry.Details, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "FAKE_PASSWORD",
            entry.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "FAKE_COOKIE",
            entry.Details,
            StringComparison.Ordinal);
        Assert.Equal(
            DateTimeOffset.Parse("2026-07-31T08:20:01.250Z"),
            entry.TimestampUtc);
    }

    [Fact]
    public async Task Pipeline_OverflowPreservesNewestEntriesAndReportsDrop()
    {
        var sink = new RecordingLogSink();
        var pipeline = CreatePipeline(
            sink,
            new AgentLoggingOptions
            {
                IngressCapacity = 2,
                SinkCapacity = 4,
            });
        var healthChanges = new List<LogPipelineHealth>();
        pipeline.HealthChanged += (_, health) => healthChanges.Add(health);

        Assert.True(pipeline.TryWrite(
            AgentLogLevel.Information,
            AgentLogCategory.General,
            "first"));
        Assert.True(pipeline.TryWrite(
            AgentLogLevel.Information,
            AgentLogCategory.General,
            "second"));
        Assert.True(pipeline.TryWrite(
            AgentLogLevel.Information,
            AgentLogCategory.General,
            "third"));
        await pipeline.StartAsync(CancellationToken.None);
        await pipeline.StopAsync(CancellationToken.None);

        Assert.Equal(
            ["second", "third"],
            sink.Entries.Select(entry => entry.Message));
        Assert.Contains(
            healthChanges,
            health =>
                health.State == LoggingHealthState.Degraded &&
                health.DroppedEntries == 1);
    }

    [Fact]
    public async Task Pipeline_FailingSinkDoesNotBlockHealthySink()
    {
        var recording = new RecordingLogSink();
        var pipeline = CreatePipeline(
            [new ThrowingLogSink(), recording]);
        var healthChanges = new List<LogPipelineHealth>();
        pipeline.HealthChanged += (_, health) => healthChanges.Add(health);
        await pipeline.StartAsync(CancellationToken.None);

        Assert.True(pipeline.TryWrite(
            AgentLogLevel.Error,
            AgentLogCategory.Error,
            "Safe failure."));
        await pipeline.StopAsync(CancellationToken.None);

        Assert.Equal("Safe failure.", Assert.Single(recording.Entries).Message);
        Assert.Contains(
            healthChanges,
            health => health.State == LoggingHealthState.Degraded);
    }

    [Fact]
    public async Task Pipeline_StopDrainsAcceptedEntries()
    {
        var sink = new RecordingLogSink();
        var pipeline = CreatePipeline(sink);
        await pipeline.StartAsync(CancellationToken.None);
        for (var index = 0; index < 20; index++)
        {
            Assert.True(pipeline.TryWrite(
                AgentLogLevel.Information,
                AgentLogCategory.General,
                $"entry-{index}"));
        }

        await pipeline.StopAsync(CancellationToken.None);

        Assert.Equal(20, sink.Entries.Count);
        Assert.Equal(
            Enumerable.Range(0, 20).Select(index => $"entry-{index}"),
            sink.Entries.Select(entry => entry.Message));
        Assert.Equal(
            LoggingHealthState.Stopped,
            pipeline.CurrentHealth.State);
    }

    [Fact]
    public async Task LoggerProvider_RoutesFormattedExceptionThroughSanitizer()
    {
        var sink = new RecordingLogSink();
        var pipeline = CreatePipeline(sink);
        await pipeline.StartAsync(CancellationToken.None);
        using var provider = new ChannelLoggerProvider(pipeline);
        ILogger logger = provider.CreateLogger("RetwhoConnector.Tests");

        logger.LogError(
            new InvalidOperationException("cookie=FAKE_COOKIE"),
            "password={Password}",
            "FAKE_PASSWORD");
        await pipeline.StopAsync(CancellationToken.None);

        LogEntry entry = Assert.Single(sink.Entries);
        Assert.Equal("password=<redacted>", entry.Message);
        Assert.DoesNotContain(
            "FAKE_COOKIE",
            entry.Details,
            StringComparison.Ordinal);
        Assert.Equal(AgentLogCategory.Error, entry.Category);
    }

    [Fact]
    public async Task UiBuffer_RetainsExactlyNewestThousandEntries()
    {
        var sink = new UiLogBufferSink(maximumEntries: 1_000);
        for (var index = 0; index < 1_005; index++)
        {
            await sink.WriteAsync(
                new LogEntry(
                    DateTimeOffset.UtcNow,
                    AgentLogLevel.Information,
                    AgentLogCategory.General,
                    $"entry-{index}"),
                CancellationToken.None);
        }

        IReadOnlyList<LogEntry> snapshot = sink.GetSnapshot();

        Assert.Equal(1_000, snapshot.Count);
        Assert.Equal("entry-5", snapshot[0].Message);
        Assert.Equal("entry-1004", snapshot[^1].Message);
    }

    private static AgentLogPipeline CreatePipeline(
        IAgentLogSink sink,
        AgentLoggingOptions? options = null) =>
        CreatePipeline([sink], options);

    private static AgentLogPipeline CreatePipeline(
        IEnumerable<IAgentLogSink> sinks,
        AgentLoggingOptions? options = null) =>
        new(
            sinks,
            new LogSanitizer(),
            new FixedTimeProvider(),
            options ?? new AgentLoggingOptions());

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            DateTimeOffset.Parse("2026-07-31T08:20:01.250Z");
    }

    private sealed class RecordingLogSink : IAgentLogSink
    {
        public string Name => "recording";
        public List<LogEntry> Entries { get; } = [];

        public ValueTask WriteAsync(
            LogEntry entry,
            CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return ValueTask.CompletedTask;
        }

        public ValueTask FlushAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class ThrowingLogSink : IAgentLogSink
    {
        public string Name => "throwing";

        public ValueTask WriteAsync(
            LogEntry entry,
            CancellationToken cancellationToken) =>
            ValueTask.FromException(
                new IOException("The fake sink failed."));

        public ValueTask FlushAsync(CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
