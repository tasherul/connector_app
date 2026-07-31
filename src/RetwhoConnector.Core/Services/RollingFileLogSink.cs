using System.Globalization;
using System.Text;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Configuration;
using RetwhoConnector.Core.Models;

namespace RetwhoConnector.Core.Services;

public sealed class RollingFileLogSink :
    IAgentLogSink,
    IAsyncDisposable
{
    private readonly LogStorageOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private FileStream? _stream;
    private string? _currentDate;
    private int _segment;
    private DateOnly _lastCleanupDate;
    private int _disposed;

    public RollingFileLogSink(
        LogStorageOptions options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        options.Validate();
        _options = options;
        _timeProvider = timeProvider;
    }

    public string Name => "file";

    public async ValueTask WriteAsync(
        LogEntry entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        byte[] bytes = Encoding.UTF8.GetBytes(Format(entry));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CleanupExpiredFilesWhenDue();
            string date = entry.TimestampUtc.ToString(
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture);
            await EnsureStreamAsync(
                date,
                bytes.LongLength,
                cancellationToken).ConfigureAwait(false);
            await _stream!.WriteAsync(bytes, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_stream is not null)
            {
                await _stream.FlushAsync(cancellationToken)
                    .ConfigureAwait(false);
                _stream.Flush(flushToDisk: true);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureStreamAsync(
        string date,
        long incomingBytes,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(_currentDate, date, StringComparison.Ordinal))
        {
            await CloseStreamAsync(cancellationToken).ConfigureAwait(false);
            _currentDate = date;
            _segment = FindLatestSegment(date);
            _stream = OpenStream(GetPath(date, _segment));
        }

        if (_stream!.Length > 0 &&
            _stream.Length + incomingBytes > _options.MaximumFileBytes)
        {
            await CloseStreamAsync(cancellationToken).ConfigureAwait(false);
            _segment++;
            _stream = OpenStream(GetPath(date, _segment));
        }
    }

    private FileStream OpenStream(string path)
    {
        Directory.CreateDirectory(_options.LogDirectory);
        return new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
    }

    private int FindLatestSegment(string date)
    {
        if (!Directory.Exists(_options.LogDirectory))
        {
            return 0;
        }

        int maximum = 0;
        foreach (string path in Directory.EnumerateFiles(
                     _options.LogDirectory,
                     $"agent-{date}*.log"))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            int separator = name.LastIndexOf('_');
            if (separator >= 0 &&
                int.TryParse(
                    name[(separator + 1)..],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int segment))
            {
                maximum = Math.Max(maximum, segment);
            }
        }

        return maximum;
    }

    private string GetPath(string date, int segment) =>
        Path.Combine(
            _options.LogDirectory,
            segment == 0
                ? $"agent-{date}.log"
                : $"agent-{date}_{segment:D3}.log");

    private void CleanupExpiredFilesWhenDue()
    {
        DateOnly today = DateOnly.FromDateTime(
            _timeProvider.GetUtcNow().UtcDateTime);
        if (_lastCleanupDate == today)
        {
            return;
        }

        _lastCleanupDate = today;
        if (!Directory.Exists(_options.LogDirectory))
        {
            return;
        }

        DateTime cutoff = _timeProvider
            .GetUtcNow()
            .AddDays(-_options.FileRetentionDays)
            .UtcDateTime;
        foreach (string path in Directory.EnumerateFiles(
                     _options.LogDirectory,
                     "agent-*.log"))
        {
            if (File.GetLastWriteTimeUtc(path) < cutoff)
            {
                File.Delete(path);
            }
        }
    }

    private static string Format(LogEntry entry)
    {
        string correlation = entry.CorrelationId is null
            ? string.Empty
            : $" [{entry.CorrelationId}]";
        string details = entry.Details is null
            ? string.Empty
            : $" | details={Escape(entry.Details)}";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"[{entry.TimestampUtc:yyyy-MM-dd HH:mm:ss.fff'Z'} " +
            $"{LevelCode(entry.Level)} {entry.Category}]" +
            $"{correlation} {Escape(entry.Message)}{details}\n");
    }

    private static string Escape(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

    private static string LevelCode(AgentLogLevel level) =>
        level switch
        {
            AgentLogLevel.Trace => "TRC",
            AgentLogLevel.Debug => "DBG",
            AgentLogLevel.Information => "INF",
            AgentLogLevel.Warning => "WRN",
            AgentLogLevel.Error => "ERR",
            AgentLogLevel.Critical => "FTL",
            _ => "INF",
        };

    private async Task CloseStreamAsync(CancellationToken cancellationToken)
    {
        if (_stream is null)
        {
            return;
        }

        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        await _stream.DisposeAsync().ConfigureAwait(false);
        _stream = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await CloseStreamAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
