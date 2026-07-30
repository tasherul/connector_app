using System.Text.Json;
using SocketIOClient;
using SocketIOClient.Common;
using SocketIOClient.Serializer.SystemTextJson;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Configuration;
using RetwhoConnector.Core.Serialization;

namespace RetwhoConnector.Core.Services;

internal sealed class SocketIoClientAdapterFactory(
    BridgeOptions options) : ISocketIoClientAdapterFactory
{
    public ISocketIoClientAdapter Create(string licenseKey)
    {
        var socketOptions = new SocketIOOptions
        {
            Path = options.Path,
            EIO = EngineIO.V4,
            Transport = TransportProtocol.WebSocket,
            AutoUpgrade = false,
            Reconnection = true,
            ReconnectionAttempts = int.MaxValue,
            ReconnectionDelayMax = 10_000,
            ConnectionTimeout = TimeSpan.FromSeconds(10),
            Auth = new Dictionary<string, string>
            {
                ["licenseKey"] = licenseKey,
            },
        };
        var socket = new SocketIO(
            options.Url,
            socketOptions,
            services => services.AddSystemTextJson(
                new JsonSerializerOptions(ConnectorJson.Options)));
        return new SocketIoClientAdapter(socket);
    }
}

internal sealed class SocketIoClientAdapter : ISocketIoClientAdapter
{
    private readonly SocketIO _socket;

    public SocketIoClientAdapter(SocketIO socket)
    {
        _socket = socket;
        _socket.OnConnected += (_, _) => _ = InvokeAsync(ConnectedEvent);
        _socket.OnDisconnected += (_, reason) =>
            _ = InvokeAsync(DisconnectedEvent, reason);
        _socket.OnError += (_, error) => _ = InvokeAsync(ErrorEvent, error);
        _socket.OnReconnectAttempt += (_, attempt) =>
            _ = InvokeAsync(ReconnectAttemptEvent, attempt);
    }

    public bool Connected => _socket.Connected;
    public event Func<Task>? ConnectedEvent;
    public event Func<string, Task>? DisconnectedEvent;
    public event Func<string, Task>? ErrorEvent;
    public event Func<int, Task>? ReconnectAttemptEvent;

    public void On(
        string eventName,
        Func<ISocketEventContext, Task> handler) =>
        _socket.On(
            eventName,
            context => handler(new SocketEventContext(context)));

    public Task ConnectAsync(CancellationToken cancellationToken) =>
        _socket.ConnectAsync(cancellationToken);

    public Task DisconnectAsync(CancellationToken cancellationToken) =>
        _socket.DisconnectAsync(cancellationToken);

    public async Task<TAck> EmitWithAckAsync<TAck>(
        string eventName,
        object payload,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var completion =
            new TaskCompletionSource<TAck>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration =
            timeoutSource.Token.Register(
                () => completion.TrySetCanceled(timeoutSource.Token));
        await _socket.EmitAsync(
            eventName,
            [payload],
            message =>
            {
                TAck? value = message.GetValue<TAck>(0);
                if (value is null)
                {
                    completion.TrySetException(
                        new JsonException("Socket acknowledgement is empty."));
                }
                else
                {
                    completion.TrySetResult(value);
                }

                return Task.CompletedTask;
            },
            timeoutSource.Token).ConfigureAwait(false);
        return await completion.Task.ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        _socket.Dispose();
        return ValueTask.CompletedTask;
    }

    private static async Task InvokeAsync(Func<Task>? handler)
    {
        if (handler is not null)
        {
            await handler.Invoke().ConfigureAwait(false);
        }
    }

    private static async Task InvokeAsync<T>(
        Func<T, Task>? handler,
        T value)
    {
        if (handler is not null)
        {
            await handler.Invoke(value).ConfigureAwait(false);
        }
    }

    private sealed class SocketEventContext(
        IEventContext context) : ISocketEventContext
    {
        public T? GetValue<T>(int index) => context.GetValue<T>(index);

        public Task SendAckDataAsync(
            object response,
            CancellationToken cancellationToken) =>
            context.SendAckDataAsync([response], cancellationToken);
    }
}
