using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Models;

namespace RetwhoConnector.Core.Services;

public sealed class ActionExecutionRegistry : IActionExecutionRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries =
        new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly CancellationToken _shutdownToken;
    private readonly TimeSpan _retention;
    private readonly int _maximumEntries;

    public ActionExecutionRegistry(
        TimeProvider timeProvider,
        CancellationToken shutdownToken,
        TimeSpan? retention = null,
        int maximumEntries = 500)
    {
        _timeProvider = timeProvider;
        _shutdownToken = shutdownToken;
        _retention = retention ?? TimeSpan.FromMinutes(15);
        _maximumEntries = maximumEntries;
    }

    public async Task<BridgeAcknowledgement> ExecuteAsync(
        string actionId,
        Func<CancellationToken, Task<BridgeAcknowledgement>> factory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        ArgumentNullException.ThrowIfNull(factory);

        Entry entry;
        lock (_gate)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            RemoveExpired(now);
            if (!_entries.TryGetValue(actionId, out entry!))
            {
                MakeRoom();
                if (_entries.Count >= _maximumEntries)
                {
                    return BridgeAcknowledgement.Failure(
                        "INTERNAL_ERROR: The connector is temporarily busy.");
                }

                Task<BridgeAcknowledgement> task = ExecuteFactoryAsync(factory);
                entry = new Entry(task, now);
                _entries.Add(actionId, entry);
                _ = ObserveCompletionAsync(entry);
            }
        }

        return await entry.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<BridgeAcknowledgement> ExecuteFactoryAsync(
        Func<CancellationToken, Task<BridgeAcknowledgement>> factory) =>
        await factory(_shutdownToken).ConfigureAwait(false);

    private async Task ObserveCompletionAsync(Entry entry)
    {
        try
        {
            await entry.Task.ConfigureAwait(false);
        }
        catch
        {
            // The completed task remains reusable for the retention period.
        }
        finally
        {
            lock (_gate)
            {
                entry.CompletedAtUtc = _timeProvider.GetUtcNow();
            }
        }
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        string[] expired = _entries
            .Where(pair =>
                pair.Value.CompletedAtUtc is DateTimeOffset completed &&
                now - completed >= _retention)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (string key in expired)
        {
            _entries.Remove(key);
        }
    }

    private void MakeRoom()
    {
        if (_entries.Count < _maximumEntries)
        {
            return;
        }

        KeyValuePair<string, Entry>? oldest = _entries
            .Where(pair => pair.Value.CompletedAtUtc is not null)
            .OrderBy(pair => pair.Value.CompletedAtUtc)
            .ThenBy(pair => pair.Value.CreatedAtUtc)
            .Cast<KeyValuePair<string, Entry>?>()
            .FirstOrDefault();
        if (oldest is { } value)
        {
            _entries.Remove(value.Key);
        }
    }

    private sealed class Entry(
        Task<BridgeAcknowledgement> task,
        DateTimeOffset createdAtUtc)
    {
        public Task<BridgeAcknowledgement> Task { get; } = task;
        public DateTimeOffset CreatedAtUtc { get; } = createdAtUtc;
        public DateTimeOffset? CompletedAtUtc { get; set; }
    }
}
