using Microsoft.Data.Sqlite;
using RetwhoConnector.Core.Configuration;
using RetwhoConnector.Core.Models;
using RetwhoConnector.Core.Security;
using RetwhoConnector.Core.Services;

namespace RetwhoConnector.Tests;

public sealed class LogPersistenceTests
{
    [Fact]
    public async Task RollingFileSink_WritesOneLineAndRollsAtSizeLimit()
    {
        using var temporary = new TemporaryDirectory();
        var options = CreateOptions(temporary.Path) with
        {
            MaximumFileBytes = 180,
        };
        await using (var sink = new RollingFileLogSink(
                         options,
                         new FixedTimeProvider()))
        {
            LogEntry first = CreateEntry(
                "first-" + new string('a', 70),
                "line-one\nline-two");
            LogEntry second = CreateEntry(
                "second-" + new string('b', 70));

            await sink.WriteAsync(first, CancellationToken.None);
            await sink.WriteAsync(second, CancellationToken.None);
            await sink.FlushAsync(CancellationToken.None);
        }

        string[] files = Directory.GetFiles(
            options.LogDirectory,
            "agent-2026-07-31*.log");
        Assert.Equal(2, files.Length);
        Assert.All(
            files,
            file => Assert.InRange(
                new FileInfo(file).Length,
                1,
                options.MaximumFileBytes));
        string combined = string.Join(
            Environment.NewLine,
            files.Order().Select(File.ReadAllText));
        Assert.Contains("line-one\\nline-two", combined, StringComparison.Ordinal);
        Assert.DoesNotContain(
            $"{Environment.NewLine}line-two",
            combined,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RollingFileSink_DeletesFilesPastRetention()
    {
        using var temporary = new TemporaryDirectory();
        LogStorageOptions options = CreateOptions(temporary.Path);
        Directory.CreateDirectory(options.LogDirectory);
        string expired = Path.Combine(
            options.LogDirectory,
            "agent-2026-07-01.log");
        string unrelated = Path.Combine(
            options.LogDirectory,
            "operator-notes.log");
        await File.WriteAllTextAsync(expired, "old");
        await File.WriteAllTextAsync(unrelated, "keep");
        File.SetLastWriteTimeUtc(
            expired,
            DateTimeOffset.Parse("2026-07-01T00:00:00Z").UtcDateTime);
        await using var sink = new RollingFileLogSink(
            options,
            new FixedTimeProvider());

        await sink.WriteAsync(
            CreateEntry("current"),
            CancellationToken.None);
        await sink.FlushAsync(CancellationToken.None);

        Assert.False(File.Exists(expired));
        Assert.True(File.Exists(unrelated));
    }

    [Fact]
    public async Task SqliteSink_StoresOnlySanitizedPipelineEntries()
    {
        using var temporary = new TemporaryDirectory();
        LogStorageOptions options = CreateOptions(temporary.Path);
        await using var sink = new SqliteLogSink(
            options,
            new FixedTimeProvider());
        var pipeline = new AgentLogPipeline(
            [sink],
            new LogSanitizer(),
            new FixedTimeProvider(),
            new AgentLoggingOptions());
        await pipeline.StartAsync(CancellationToken.None);

        Assert.True(pipeline.TryWrite(
            AgentLogLevel.Error,
            AgentLogCategory.Error,
            "password=FAKE_PASSWORD",
            """{"cookie":"FAKE_COOKIE"}""",
            "action-1"));
        await pipeline.StopAsync(CancellationToken.None);

        await using var connection = CreateTestConnection(options.DatabasePath);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT message, details, correlation_id FROM log_entries;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("password=<redacted>", reader.GetString(0));
        Assert.Equal(
            """{"cookie":"<redacted>"}""",
            reader.GetString(1));
        Assert.Equal("action-1", reader.GetString(2));
        Assert.DoesNotContain(
            "FAKE_",
            $"{reader.GetString(0)}{reader.GetString(1)}",
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SqliteSink_UsesVersionedWalSchema()
    {
        using var temporary = new TemporaryDirectory();
        LogStorageOptions options = CreateOptions(temporary.Path);
        await using var sink = new SqliteLogSink(
            options,
            new FixedTimeProvider());

        await sink.WriteAsync(
            CreateEntry("schema"),
            CancellationToken.None);
        await sink.FlushAsync(CancellationToken.None);

        await using var connection = CreateTestConnection(options.DatabasePath);
        await connection.OpenAsync();
        Assert.Equal(
            "wal",
            await ExecuteScalarAsync<string>(
                connection,
                "PRAGMA journal_mode;"));
        Assert.Equal(
            1L,
            await ExecuteScalarAsync<long>(
                connection,
                "PRAGMA user_version;"));
        Assert.Equal(
            1L,
            await ExecuteScalarAsync<long>(
                connection,
                "SELECT COUNT(*) FROM log_entries;"));
    }

    [Fact]
    public async Task SqliteSink_TrimsExpiredAndOldestRows()
    {
        using var temporary = new TemporaryDirectory();
        LogStorageOptions options = CreateOptions(temporary.Path) with
        {
            MaximumDatabaseRows = 3,
        };
        await using var sink = new SqliteLogSink(
            options,
            new FixedTimeProvider());
        await sink.WriteAsync(
            CreateEntry(
                "expired",
                timestamp: DateTimeOffset.Parse("2026-06-01T00:00:00Z")),
            CancellationToken.None);
        for (var index = 0; index < 5; index++)
        {
            await sink.WriteAsync(
                CreateEntry(
                    $"current-{index}",
                    timestamp: DateTimeOffset.Parse(
                        $"2026-07-31T08:20:0{index}Z")),
                CancellationToken.None);
        }

        await sink.FlushAsync(CancellationToken.None);

        await using var connection = CreateTestConnection(options.DatabasePath);
        await connection.OpenAsync();
        Assert.Equal(
            3L,
            await ExecuteScalarAsync<long>(
                connection,
                "SELECT COUNT(*) FROM log_entries;"));
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT message FROM log_entries ORDER BY timestamp_utc;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        var messages = new List<string>();
        while (await reader.ReadAsync())
        {
            messages.Add(reader.GetString(0));
        }

        Assert.Equal(
            ["current-2", "current-3", "current-4"],
            messages);
    }

    private static LogStorageOptions CreateOptions(string root) =>
        new()
        {
            LogDirectory = Path.Combine(root, "Logs"),
            DatabasePath = Path.Combine(root, "Data", "agent.db"),
            MaximumFileBytes = 10 * 1024 * 1024,
            FileRetentionDays = 14,
            DatabaseRetentionDays = 30,
            MaximumDatabaseRows = 100_000,
            DatabaseBatchSize = 100,
            DatabaseBatchInterval = TimeSpan.FromMilliseconds(500),
        };

    private static LogEntry CreateEntry(
        string message,
        string? details = null,
        DateTimeOffset? timestamp = null) =>
        new(
            timestamp ??
            DateTimeOffset.Parse("2026-07-31T08:20:01.250Z"),
            AgentLogLevel.Information,
            AgentLogCategory.General,
            message,
            details,
            "action-1");

    private static async Task<T> ExecuteScalarAsync<T>(
        SqliteConnection connection,
        string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = await command.ExecuteScalarAsync();
        return Assert.IsType<T>(value);
    }

    private static SqliteConnection CreateTestConnection(string databasePath) =>
        new(
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWrite,
                Cache = SqliteCacheMode.Private,
                Pooling = false,
            }.ToString());

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            DateTimeOffset.Parse("2026-07-31T08:20:01.250Z");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "RetwhoConnector.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
