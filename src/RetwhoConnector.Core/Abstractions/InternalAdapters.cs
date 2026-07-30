using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace RetwhoConnector.Core.Abstractions;

internal interface ISettingsFileStore
{
    Task<string?> ReadAsync(string path, CancellationToken cancellationToken);
    Task WriteAtomicAsync(string path, string content, CancellationToken cancellationToken);
    Task DeleteAsync(string path, CancellationToken cancellationToken);
}

internal interface ICertificateProbe
{
    Task<(X509Certificate2 Certificate, SslPolicyErrors PolicyErrors)> InspectAsync(
        Uri origin,
        CancellationToken cancellationToken);
}

internal interface ISocketEventContext
{
    T? GetValue<T>(int index);
    Task SendAckDataAsync(object response, CancellationToken cancellationToken);
}

internal interface ISocketIoClientAdapter : IAsyncDisposable
{
    bool Connected { get; }
    event Func<Task>? ConnectedEvent;
    event Func<string, Task>? DisconnectedEvent;
    event Func<string, Task>? ErrorEvent;
    event Func<int, Task>? ReconnectAttemptEvent;

    void On(string eventName, Func<ISocketEventContext, Task> handler);
    Task ConnectAsync(CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
    Task<TAck> EmitWithAckAsync<TAck>(
        string eventName,
        object payload,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

internal interface ISocketIoClientAdapterFactory
{
    ISocketIoClientAdapter Create(string licenseKey);
}
