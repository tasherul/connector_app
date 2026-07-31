using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Configuration;
using RetwhoConnector.Core.Models;

namespace RetwhoConnector.Core.Services;

public sealed class AgentLogPipeline :
    IAgentLog,
    IHostedService,
    IAsyncDisposable
{
    private readonly ILogSanitizer _sanitizer;
    private readonly TimeProvider _timeProvider;
    private readonly AgentLoggingOptions _options;
    private readonly Channel<LogEntry> _ingress;
    private readonly IReadOnlyList<SinkWorker> _workers;
    private readonly object _healthGate = new();
    private LogPipelineHealth _currentHealth =
        new(LoggingHealthState.Stopped, 0, "Logging is stopped.");
    private Task? _dispatchTask;
    private long _droppedEntries;
    private int _started;
    private int _accepting = 1;

    public AgentLogPipeline(
        IEnumerable<IAgentLogSink> sinks,
        ILogSanitizer sanitizer,
        TimeProvider timeProvider,
        AgentLoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(sinks);
        ArgumentNullException.ThrowIfNull(sanitizer);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _sanitizer = sanitizer;
        _timeProvider = timeProvider;
        _options = options;
        _ingress = Channel.CreateBounded<LogEntry>(
            new BoundedChannelOptions(options.IngressCapacity)
            {
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            },
            _ => ReportDrop("The agent log queue dropped an entry."));
        _workers = sinks
            .Select(sink => new SinkWorker(
                sink,
                options.SinkCapacity,
                () => ReportDrop(
                    $"The {sink.Name} log queue dropped an entry."),
                failed => ReportSinkState(sink.Name, failed)))
            .ToArray();
    }

    public LogPipelineHealth CurrentHealth
    {
        get
        {
            lock (_healthGate)
            {
                return _currentHealth;
            }
        }
    }

    public event EventHandler<LogPipelineHealth>? HealthChanged;

    public bool TryWrite(
        AgentLogLevel level,
        AgentLogCategory category,
        string message,
        string? details = null,
        string? correlationId = null)
    {
        if (Volatile.Read(ref _accepting) == 0)
        {
            return false;
        }

        try
        {
            string safeMessage = Truncate(
                _sanitizer.Sanitize(message),
                _options.MaximumMessageCharacters);
            string? safeDetails = details is null
                ? null
                : Truncate(
                    _sanitizer.Sanitize(details),
                    _options.MaximumDetailsCharacters);
            var entry = new LogEntry(
                _timeProvider.GetUtcNow(),
                level,
                category,
                safeMessage,
                safeDetails,
                correlationId);
            return _ingress.Writer.TryWrite(entry);
        }
        catch (ArgumentException)
        {
            ReportDrop("An invalid log entry was rejected.");
            return false;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return Task.CompletedTask;
        }

        foreach (SinkWorker worker in _workers)
        {
            worker.Start();
        }

        if (Interlocked.Read(ref _droppedEntries) == 0)
        {
            UpdateHealth(
                LoggingHealthState.Healthy,
                "Logging is healthy.");
        }

        _dispatchTask = DispatchAsync();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _accepting, 0);
        _ingress.Writer.TryComplete();
        if (_dispatchTask is not null)
        {
            await _dispatchTask.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            foreach (SinkWorker worker in _workers)
            {
                worker.Complete();
            }

            await Task.WhenAll(_workers.Select(worker => worker.Completion))
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        UpdateHealth(
            LoggingHealthState.Stopped,
            "Logging is stopped.");
    }

    private async Task DispatchAsync()
    {
        await foreach (LogEntry entry in _ingress.Reader.ReadAllAsync())
        {
            foreach (SinkWorker worker in _workers)
            {
                worker.TryWrite(entry);
            }
        }

        foreach (SinkWorker worker in _workers)
        {
            worker.Complete();
        }

        await Task.WhenAll(_workers.Select(worker => worker.Completion))
            .ConfigureAwait(false);
    }

    private void ReportDrop(string message)
    {
        Interlocked.Increment(ref _droppedEntries);
        UpdateHealth(LoggingHealthState.Degraded, message);
    }

    private void ReportSinkState(string sinkName, bool failed)
    {
        if (failed)
        {
            Interlocked.Increment(ref _droppedEntries);
            UpdateHealth(
                LoggingHealthState.Degraded,
                $"The {sinkName} log sink failed.");
            return;
        }

        if (_workers.All(worker => !worker.HasFailed))
        {
            UpdateHealth(
                LoggingHealthState.Healthy,
                "Logging recovered.");
        }
    }

    private void UpdateHealth(
        LoggingHealthState state,
        string message)
    {
        var health = new LogPipelineHealth(
            state,
            Interlocked.Read(ref _droppedEntries),
            message);
        lock (_healthGate)
        {
            _currentHealth = health;
        }

        HealthChanged?.Invoke(this, health);
    }

    private static string Truncate(string value, int maximumCharacters) =>
        value.Length <= maximumCharacters
            ? value
            : string.Concat(value.AsSpan(0, maximumCharacters - 1), "…");

    public async ValueTask DisposeAsync()
    {
        if (Volatile.Read(ref _accepting) != 0)
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private sealed class SinkWorker
    {
        private readonly IAgentLogSink _sink;
        private readonly Channel<LogEntry> _channel;
        private readonly Action<bool> _stateChanged;
        private Task? _runTask;
        private int _failed;

        public SinkWorker(
            IAgentLogSink sink,
            int capacity,
            Action dropped,
            Action<bool> stateChanged)
        {
            _sink = sink;
            _stateChanged = stateChanged;
            _channel = Channel.CreateBounded<LogEntry>(
                new BoundedChannelOptions(capacity)
                {
                    AllowSynchronousContinuations = false,
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = true,
                },
                _ => dropped());
        }

        public bool HasFailed => Volatile.Read(ref _failed) != 0;

        public Task Completion => _runTask ?? Task.CompletedTask;

        public void Start() => _runTask = RunAsync();

        public bool TryWrite(LogEntry entry) =>
            _channel.Writer.TryWrite(entry);

        public void Complete() => _channel.Writer.TryComplete();

        private async Task RunAsync()
        {
            await foreach (LogEntry entry in _channel.Reader.ReadAllAsync())
            {
                try
                {
                    await _sink.WriteAsync(
                        entry,
                        CancellationToken.None).ConfigureAwait(false);
                    if (Interlocked.Exchange(ref _failed, 0) != 0)
                    {
                        _stateChanged(false);
                    }
                }
                catch (Exception)
                {
                    Interlocked.Exchange(ref _failed, 1);
                    _stateChanged(true);
                }
            }

            try
            {
                await _sink.FlushAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                Interlocked.Exchange(ref _failed, 1);
                _stateChanged(true);
            }
        }
    }
}
