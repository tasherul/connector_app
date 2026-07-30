namespace RetwhoConnector.Core.Models;

public sealed class BridgeActionContext
{
    private readonly Func<BridgeAcknowledgement, CancellationToken, Task> _acknowledge;
    private int _acknowledged;

    public BridgeActionContext(
        BridgeAction action,
        Func<BridgeAcknowledgement, CancellationToken, Task> acknowledge,
        CancellationToken sessionCancellationToken)
    {
        Action = action ?? throw new ArgumentNullException(nameof(action));
        _acknowledge = acknowledge ?? throw new ArgumentNullException(nameof(acknowledge));
        SessionCancellationToken = sessionCancellationToken;
    }

    public BridgeAction Action { get; }
    public CancellationToken SessionCancellationToken { get; }
    public bool IsAcknowledged => Volatile.Read(ref _acknowledged) != 0;

    public Task AcknowledgeOnceAsync(
        BridgeAcknowledgement acknowledgement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(acknowledgement);
        return Interlocked.Exchange(ref _acknowledged, 1) == 0
            ? _acknowledge(acknowledgement, cancellationToken)
            : Task.CompletedTask;
    }
}
