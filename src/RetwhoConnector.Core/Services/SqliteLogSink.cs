using System.Globalization;
using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Configuration;
using RetwhoConnector.Core.Models;

namespace RetwhoConnector.Core.Services;

public sealed class SqliteLogSink :
    IAgentLogSink,
    IAsyncDisposable
{
    private readonly LogStorageOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly Channel<WorkItem> _channel =
        Channel.CreateUnbounded<WorkItem>(
            new UnboundedChannelOptions
            {
                AllowSynchronousContinuations = false,
                SingleReader = true,
                SingleWriter = false,
            });
    private readonly Task _processingTask;
    private int _disposed;

    public SqliteLogSink(
        LogStorageOptions options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        options.Validate();
        _options = options;
        _timeProvider = timeProvider;
        _processingTask = ProcessAsync();
    }

    public string Name => "SQLite";

    public ValueTask WriteAsync(
        LogEntry entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        return _channel.Writer.TryWrite(new EntryWorkItem(entry))
            ? ValueTask.CompletedTask
            : ValueTask.FromException(
                new InvalidOperationException(
                    "The SQLite log sink is stopped."));
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_channel.Writer.TryWrite(
                new FlushWorkItem(completion)))
        {
            return new ValueTask(
                Task.FromException(
                    new InvalidOperationException(
                        "The SQLite log sink is stopped.")));
        }

        return new ValueTask(
            completion.Task.WaitAsync(cancellationToken));
    }

    private async Task ProcessAsync()
    {
        string? directory = Path.GetDirectoryName(_options.DatabasePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new IOException("The log database path has no directory.");
        }

        Directory.CreateDirectory(directory);
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = _options.DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
                Pooling = false,
            }.ToString());
        var batch = new List<LogEntry>(_options.DatabaseBatchSize);
        try
        {
            await connection.OpenAsync().ConfigureAwait(false);
            await InitializeAsync(connection).ConfigureAwait(false);
            while (true)
            {
                WorkItem work;
                try
                {
                    work = batch.Count == 0
                        ? await _channel.Reader.ReadAsync().ConfigureAwait(false)
                        : await ReadWithBatchTimeoutAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    await InsertBatchAsync(connection, batch)
                        .ConfigureAwait(false);
                    batch.Clear();
                    continue;
                }
                catch (ChannelClosedException)
                {
                    break;
                }

                switch (work)
                {
                    case EntryWorkItem entry:
                        batch.Add(entry.Entry);
                        if (batch.Count >= _options.DatabaseBatchSize)
                        {
                            await InsertBatchAsync(connection, batch)
                                .ConfigureAwait(false);
                            batch.Clear();
                        }

                        break;
                    case FlushWorkItem flush:
                        try
                        {
                            await InsertBatchAsync(connection, batch)
                                .ConfigureAwait(false);
                            batch.Clear();
                            await RunMaintenanceAsync(connection)
                                .ConfigureAwait(false);
                            flush.Completion.TrySetResult();
                        }
                        catch (Exception exception)
                        {
                            flush.Completion.TrySetException(exception);
                            throw;
                        }

                        break;
                }
            }

            await InsertBatchAsync(connection, batch).ConfigureAwait(false);
            await RunMaintenanceAsync(connection).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _channel.Writer.TryComplete(exception);
            while (_channel.Reader.TryRead(out WorkItem? remaining))
            {
                if (remaining is FlushWorkItem flush)
                {
                    flush.Completion.TrySetException(exception);
                }
            }

            throw;
        }
    }

    private async ValueTask<WorkItem> ReadWithBatchTimeoutAsync()
    {
        using var source = new CancellationTokenSource(
            _options.DatabaseBatchInterval);
        return await _channel.Reader.ReadAsync(source.Token)
            .ConfigureAwait(false);
    }

    private static async Task InitializeAsync(SqliteConnection connection)
    {
        await ExecuteNonQueryAsync(
            connection,
            """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            PRAGMA busy_timeout=5000;
            """).ConfigureAwait(false);
        long version = await ExecuteScalarAsync<long>(
            connection,
            "PRAGMA user_version;").ConfigureAwait(false);
        if (version is not 0 and not 1)
        {
            throw new InvalidDataException(
                $"Unsupported log database schema version {version}.");
        }

        await ExecuteNonQueryAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS log_entries (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp_utc TEXT NOT NULL,
                level INTEGER NOT NULL,
                category INTEGER NOT NULL,
                message TEXT NOT NULL,
                details TEXT NULL,
                correlation_id TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_log_entries_timestamp_utc
                ON log_entries(timestamp_utc);
            PRAGMA user_version=1;
            """).ConfigureAwait(false);
    }

    private static async Task InsertBatchAsync(
        SqliteConnection connection,
        IReadOnlyCollection<LogEntry> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection
                .BeginTransactionAsync()
                .ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO log_entries (
                timestamp_utc,
                level,
                category,
                message,
                details,
                correlation_id)
            VALUES (
                $timestamp,
                $level,
                $category,
                $message,
                $details,
                $correlation);
            """;
        SqliteParameter timestamp = command.Parameters.Add(
            "$timestamp",
            SqliteType.Text);
        SqliteParameter level = command.Parameters.Add(
            "$level",
            SqliteType.Integer);
        SqliteParameter category = command.Parameters.Add(
            "$category",
            SqliteType.Integer);
        SqliteParameter message = command.Parameters.Add(
            "$message",
            SqliteType.Text);
        SqliteParameter details = command.Parameters.Add(
            "$details",
            SqliteType.Text);
        SqliteParameter correlation = command.Parameters.Add(
            "$correlation",
            SqliteType.Text);
        command.Prepare();
        foreach (LogEntry entry in entries)
        {
            timestamp.Value = entry.TimestampUtc.ToString(
                "O",
                CultureInfo.InvariantCulture);
            level.Value = (int)entry.Level;
            category.Value = (int)entry.Category;
            message.Value = entry.Message;
            details.Value = entry.Details is null
                ? DBNull.Value
                : entry.Details;
            correlation.Value = entry.CorrelationId is null
                ? DBNull.Value
                : entry.CorrelationId;
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await transaction.CommitAsync().ConfigureAwait(false);
    }

    private async Task RunMaintenanceAsync(SqliteConnection connection)
    {
        string cutoff = _timeProvider
            .GetUtcNow()
            .AddDays(-_options.DatabaseRetentionDays)
            .ToString("O", CultureInfo.InvariantCulture);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM log_entries
            WHERE timestamp_utc < $cutoff;

            DELETE FROM log_entries
            WHERE id NOT IN (
                SELECT id
                FROM log_entries
                ORDER BY timestamp_utc DESC, id DESC
                LIMIT $maximumRows
            );
            """;
        command.Parameters.AddWithValue("$cutoff", cutoff);
        command.Parameters.AddWithValue(
            "$maximumRows",
            _options.MaximumDatabaseRows);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task<T> ExecuteScalarAsync<T>(
        SqliteConnection connection,
        string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = await command.ExecuteScalarAsync()
            .ConfigureAwait(false);
        object converted = Convert.ChangeType(
            value ?? throw new InvalidDataException(
                "The log database returned an empty scalar value."),
            typeof(T),
            CultureInfo.InvariantCulture);
        return (T)converted;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await FlushAsync(CancellationToken.None).ConfigureAwait(false);
        _channel.Writer.TryComplete();
        await _processingTask.ConfigureAwait(false);
    }

    private abstract record WorkItem;

    private sealed record EntryWorkItem(LogEntry Entry) : WorkItem;

    private sealed record FlushWorkItem(
        TaskCompletionSource Completion) : WorkItem;
}
