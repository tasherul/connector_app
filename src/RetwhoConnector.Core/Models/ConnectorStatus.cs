namespace RetwhoConnector.Core.Models;

public enum PosConfigurationState
{
    NotConfigured,
    Configured,
    Invalid,
}

public enum PosAuthenticationState
{
    NotConfigured,
    CertificateApprovalRequired,
    Authenticating,
    Authenticated,
    CachedSessionUnverified,
    RefreshingSession,
    AuthenticationFailed,
    CertificateChanged,
}

public enum BridgeTransportState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    AuthenticationFailed,
    SessionReplaced,
    Stopping,
}

public enum AgentRegistrationState
{
    NotRegistered,
    Registering,
    Registered,
    Failed,
    SessionReplaced,
}

public enum LastCommandState
{
    None,
    Running,
    Completed,
    Failed,
    Cancelled,
}

public sealed record ConnectorStatus
{
    public PosConfigurationState PosConfiguration { get; init; }
    public PosAuthenticationState PosAuthentication { get; init; }
    public BridgeTransportState BridgeTransport { get; init; }
    public AgentRegistrationState AgentRegistration { get; init; }
    public LastCommandState LastCommand { get; init; }
    public DateTimeOffset? LastCommandTimestamp { get; init; }
    public string Message { get; init; } = "Not configured";
}

public sealed class BridgeConnectionStateChangedEventArgs(
    BridgeTransportState transportState,
    AgentRegistrationState registrationState,
    string message) : EventArgs
{
    public BridgeTransportState TransportState { get; } = transportState;
    public AgentRegistrationState RegistrationState { get; } = registrationState;
    public string Message { get; } = message;
}
