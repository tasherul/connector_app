using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using RetwhoConnector.Core.Models;

namespace RetwhoConnector.Core.Abstractions;

public interface IBridgeSocketClient : IAsyncDisposable
{
    bool IsTransportConnected { get; }
    bool IsRegistered { get; }

    event EventHandler<BridgeConnectionStateChangedEventArgs>? StateChanged;
    event Func<BridgeActionContext, CancellationToken, Task>? ActionReceived;
    event EventHandler? SessionReplaced;

    Task ConnectAsync(string licenseKey, CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
    Task<AgentDataPushResponse> PushAgentDataAsync(
        object payload,
        CancellationToken cancellationToken);
}

public interface IPosAuthenticationService
{
    Task<PosSession> LoginAsync(
        ConnectorSettings settings,
        CancellationToken cancellationToken);
}

public interface IPosDataService
{
    Task<VdatetimeResult> GetVdatetimeAsync(
        ConnectorSettings settings,
        string cookie,
        CancellationToken cancellationToken);
}

public interface IPosHttpClient
{
    Task<PosHttpResponse> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken);
}

public interface ISecureSettingsService
{
    Task<ConnectorSettings?> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(ConnectorSettings settings, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}

public interface IVdatetimeXmlMapper
{
    VdatetimeResult Parse(string xml, DateTimeOffset fetchedAtUtc);
}

public interface ICertificateTrustService
{
    Task<PresentedCertificate> InspectAsync(
        Uri posBaseUri,
        CancellationToken cancellationToken);

    bool ValidateForRequest(
        Uri configuredPosBaseUri,
        Uri requestUri,
        X509Certificate2 certificate,
        SslPolicyErrors policyErrors,
        string? approvedSha256);
}

public interface IActionExecutionRegistry
{
    Task<BridgeAcknowledgement> ExecuteAsync(
        string actionId,
        Func<CancellationToken, Task<BridgeAcknowledgement>> factory,
        CancellationToken cancellationToken);
}

public interface IAgentLog
{
    LogPipelineHealth CurrentHealth { get; }
    event EventHandler<LogPipelineHealth>? HealthChanged;

    bool TryWrite(
        AgentLogLevel level,
        AgentLogCategory category,
        string message,
        string? details = null,
        string? correlationId = null);
}

public interface ILogSanitizer
{
    string Sanitize(string? value);
}

public interface IAgentLogSink
{
    string Name { get; }

    ValueTask WriteAsync(
        LogEntry entry,
        CancellationToken cancellationToken);

    ValueTask FlushAsync(CancellationToken cancellationToken);
}

public interface IPosResponseReader
{
    Task<PosHttpResponse> ReadAsync(
        HttpResponseMessage response,
        int maximumDecompressedBytes,
        CancellationToken cancellationToken);
}
