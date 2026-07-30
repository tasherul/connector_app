using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Configuration;
using RetwhoConnector.Core.Models;
using RetwhoConnector.Core.Serialization;
using RetwhoConnector.Core.Validation;

namespace RetwhoConnector.Core.Services;

public sealed partial class BridgeSocketClient : IBridgeSocketClient
{
    private readonly ISocketIoClientAdapterFactory _factory;
    private readonly BridgeOptions _options;
    private readonly ILogger<BridgeSocketClient> _logger;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private ISocketIoClientAdapter? _socket;
    private TaskCompletionSource<bool>? _registrationCompletion;
    private CancellationTokenSource? _sessionSource;
    private string? _licenseKey;
    private bool _allowReconnect;

    public BridgeSocketClient(
        BridgeOptions options,
        ILogger<BridgeSocketClient> logger)
        : this(new SocketIoClientAdapterFactory(options), options, logger)
    {
    }

    internal BridgeSocketClient(
        ISocketIoClientAdapterFactory factory,
        BridgeOptions options,
        ILogger<BridgeSocketClient> logger)
    {
        _factory = factory;
        _options = options;
        _logger = logger;
    }

    public bool IsTransportConnected => _socket?.Connected == true;
    public bool IsRegistered { get; private set; }

    public event EventHandler<BridgeConnectionStateChangedEventArgs>? StateChanged;
    public event Func<BridgeActionContext, CancellationToken, Task>? ActionReceived;
    public event EventHandler? SessionReplaced;

    public async Task ConnectAsync(
        string licenseKey,
        CancellationToken cancellationToken)
    {
        if (!LicensePattern().IsMatch(licenseKey))
        {
            throw new ArgumentException(
                "The license key has an invalid format.",
                nameof(licenseKey));
        }

        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DisposeSocketAsync().ConfigureAwait(false);
            _licenseKey = licenseKey;
            _allowReconnect = true;
            _sessionSource = new CancellationTokenSource();
            _registrationCompletion =
                new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            _socket = _factory.Create(licenseKey);
            WireSocket(_socket);
            RaiseState(
                BridgeTransportState.Connecting,
                AgentRegistrationState.NotRegistered,
                "Connecting to the bridge…");
            await _socket.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await _registrationCompletion.Task.WaitAsync(
                _options.RegistrationTimeout,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        _allowReconnect = false;
        IsRegistered = false;
        _sessionSource?.Cancel();
        if (_socket is not null)
        {
            await _socket.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        }

        RaiseState(
            BridgeTransportState.Disconnected,
            AgentRegistrationState.NotRegistered,
            "Disconnected from the bridge.");
    }

    public async Task<AgentDataPushResponse> PushAgentDataAsync(
        object payload,
        CancellationToken cancellationToken)
    {
        if (!IsRegistered || _socket is null)
        {
            throw new InvalidOperationException(
                "The connector is not registered.");
        }

        JsonElement element = JsonSerializer.SerializeToElement(
            payload,
            ConnectorJson.Options);
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(
                "The agent payload must be a JSON object.",
                nameof(payload));
        }

        if (JsonSerializer.SerializeToUtf8Bytes(
                payload,
                ConnectorJson.Options).Length >= _options.MaximumPayloadBytes)
        {
            throw new ArgumentException(
                "The agent payload exceeds the bridge limit.",
                nameof(payload));
        }

        BridgeEnvelope<AgentDataPushResponse> response =
            await _socket.EmitWithAckAsync<BridgeEnvelope<AgentDataPushResponse>>(
                "agent_data_push",
                payload,
                _options.ActionAcknowledgementTimeout,
                cancellationToken).ConfigureAwait(false);
        if (!response.Ok ||
            !response.Code.Equals("DATA_ACCEPTED", StringComparison.Ordinal) ||
            response.Data is null)
        {
            throw new InvalidOperationException(
                $"The bridge rejected agent data with code {response.Code}.");
        }

        return response.Data;
    }

    private void WireSocket(ISocketIoClientAdapter socket)
    {
        socket.ConnectedEvent += RegisterAsync;
        socket.DisconnectedEvent += OnDisconnectedAsync;
        socket.ErrorEvent += OnTransportErrorAsync;
        socket.ReconnectAttemptEvent += OnReconnectAttemptAsync;
        socket.On("registered", _ => Task.CompletedTask);
        socket.On("execute_local_action", HandleActionEventAsync);
        socket.On("session_replaced", HandleSessionReplacedAsync);
        socket.On("auth_error", HandleAuthErrorAsync);
    }

    private async Task RegisterAsync()
    {
        if (_socket is null || _licenseKey is null)
        {
            return;
        }

        IsRegistered = false;
        RaiseState(
            BridgeTransportState.Connected,
            AgentRegistrationState.Registering,
            "Registering connector agent…");
        try
        {
            BridgeEnvelope<RegistrationResponse> response =
                await _socket.EmitWithAckAsync<BridgeEnvelope<RegistrationResponse>>(
                    "register_client",
                    new
                    {
                        LicenseKey = _licenseKey,
                        ClientType = "localhost_agent",
                    },
                    _options.RegistrationTimeout,
                    _sessionSource?.Token ?? CancellationToken.None)
                    .ConfigureAwait(false);
            if (!response.Ok ||
                !response.Code.Equals("REGISTERED", StringComparison.Ordinal) ||
                response.Data is null ||
                !response.Data.ClientType.Equals(
                    "localhost_agent",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Registration failed with code {response.Code}.");
            }

            IsRegistered = true;
            RaiseState(
                BridgeTransportState.Connected,
                AgentRegistrationState.Registered,
                "Bridge agent registered.");
            _registrationCompletion?.TrySetResult(true);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Bridge registration failed with {ExceptionType}",
                exception.GetType().Name);
            _registrationCompletion?.TrySetException(exception);
            RaiseState(
                BridgeTransportState.Disconnected,
                AgentRegistrationState.Failed,
                "Bridge registration failed.");
        }
    }

    private async Task HandleActionEventAsync(ISocketEventContext socketContext)
    {
        BridgeActionContext? actionContext = null;
        try
        {
            BridgeAction action = socketContext.GetValue<BridgeAction>(0)
                ?? throw new JsonException("Command payload is missing.");
            BridgeActionValidator.Validate(action);
            actionContext = new BridgeActionContext(
                action,
                (response, token) =>
                    socketContext.SendAckDataAsync(response, token),
                _sessionSource?.Token ?? CancellationToken.None);
            Delegate[] handlers = ActionReceived?.GetInvocationList() ?? [];
            if (handlers.Length == 0 || !IsRegistered)
            {
                await actionContext.AcknowledgeOnceAsync(
                    BridgeAcknowledgement.Failure(
                        "NOT_REGISTERED: The connector is not registered."))
                    .ConfigureAwait(false);
                return;
            }

            foreach (Delegate handler in handlers)
            {
                await ((Func<BridgeActionContext, CancellationToken, Task>)handler)(
                    actionContext,
                    _sessionSource?.Token ?? CancellationToken.None)
                    .ConfigureAwait(false);
            }

            if (!actionContext.IsAcknowledged)
            {
                await actionContext.AcknowledgeOnceAsync(
                    BridgeAcknowledgement.Failure(
                        "INTERNAL_ERROR: The command produced no acknowledgement."))
                    .ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            BridgeAcknowledgement failure = BridgeAcknowledgement.Failure(
                actionContext is null
                    ? "INVALID_ACTION: The bridge command is invalid."
                    : "INTERNAL_ERROR: The command handler failed.");
            if (actionContext is null)
            {
                await socketContext.SendAckDataAsync(
                    failure,
                    CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                await actionContext.AcknowledgeOnceAsync(failure)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task HandleSessionReplacedAsync(ISocketEventContext _)
    {
        _allowReconnect = false;
        IsRegistered = false;
        _sessionSource?.Cancel();
        RaiseState(
            BridgeTransportState.SessionReplaced,
            AgentRegistrationState.SessionReplaced,
            "This connector session was replaced.");
        SessionReplaced?.Invoke(this, EventArgs.Empty);
        if (_socket is not null)
        {
            await _socket.DisconnectAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private async Task HandleAuthErrorAsync(ISocketEventContext context)
    {
        BridgeEnvelope<object>? error =
            context.GetValue<BridgeEnvelope<object>>(0);
        string code = error?.Code ?? "AUTH_ERROR";
        bool permanent = PermanentErrors.Contains(code);
        if (permanent)
        {
            _allowReconnect = false;
            IsRegistered = false;
            _sessionSource?.Cancel();
        }

        RaiseState(
            permanent
                ? BridgeTransportState.AuthenticationFailed
                : BridgeTransportState.Reconnecting,
            AgentRegistrationState.Failed,
            permanent
                ? $"Bridge authentication failed: {code}."
                : "The bridge is temporarily unavailable.");
        if (permanent && _socket is not null)
        {
            await _socket.DisconnectAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private Task OnDisconnectedAsync(string _)
    {
        IsRegistered = false;
        RaiseState(
            _allowReconnect
                ? BridgeTransportState.Reconnecting
                : BridgeTransportState.Disconnected,
            AgentRegistrationState.NotRegistered,
            _allowReconnect
                ? "Bridge connection lost; reconnecting…"
                : "Disconnected from the bridge.");
        return Task.CompletedTask;
    }

    private Task OnTransportErrorAsync(string _)
    {
        RaiseState(
            BridgeTransportState.Reconnecting,
            AgentRegistrationState.NotRegistered,
            "Bridge transport error; reconnecting…");
        return Task.CompletedTask;
    }

    private Task OnReconnectAttemptAsync(int _)
    {
        RaiseState(
            BridgeTransportState.Reconnecting,
            AgentRegistrationState.NotRegistered,
            "Reconnecting to the bridge…");
        return Task.CompletedTask;
    }

    private void RaiseState(
        BridgeTransportState transport,
        AgentRegistrationState registration,
        string message) =>
        StateChanged?.Invoke(
            this,
            new BridgeConnectionStateChangedEventArgs(
                transport,
                registration,
                message));

    private async Task DisposeSocketAsync()
    {
        _sessionSource?.Cancel();
        _sessionSource?.Dispose();
        _sessionSource = null;
        if (_socket is not null)
        {
            await _socket.DisposeAsync().ConfigureAwait(false);
            _socket = null;
        }

        IsRegistered = false;
    }

    public async ValueTask DisposeAsync()
    {
        _allowReconnect = false;
        await DisposeSocketAsync().ConfigureAwait(false);
        _connectionGate.Dispose();
    }

    private static readonly HashSet<string> PermanentErrors =
        new(StringComparer.Ordinal)
        {
            "LICENSE_KEY_REQUIRED",
            "INVALID_LICENSE_KEY",
            "LICENSE_NOT_ACTIVE",
            "LICENSE_KEY_MISMATCH",
            "INVALID_CLIENT_TYPE",
            "DUPLICATE_AGENT_REPLACED",
            "REGISTER_TIMEOUT",
        };

    [GeneratedRegex("^[A-Za-z0-9._:~-]{1,255}$", RegexOptions.CultureInvariant)]
    private static partial Regex LicensePattern();
}
