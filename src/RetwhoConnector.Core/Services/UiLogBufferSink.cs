using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Models;

namespace RetwhoConnector.Core.Services;

public sealed class UiLogBufferSink : IAgentLogSink
{
    private readonly int _maximumEntries;
    private readonly Queue<LogEntry> _entries;
    private readonly object _gate = new();

    public UiLogBufferSink(int maximumEntries = 1_000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumEntries, 1);
        _maximumEntries = maximumEntries;
        _entries = new Queue<LogEntry>(maximumEntries);
    }

    public string Name => "UI";

    public event EventHandler? Changed;

    public ValueTask WriteAsync(
        LogEntry entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > _maximumEntries)
            {
                _entries.Dequeue();
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return ValueTask.CompletedTask;
    }

    public IReadOnlyList<LogEntry> GetSnapshot()
    {
        lock (_gate)
        {
            return _entries.ToArray();
        }
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}
