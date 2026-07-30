using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Configuration;
using RetwhoConnector.Core.Exceptions;
using RetwhoConnector.Core.Models;
using RetwhoConnector.Core.Serialization;
using RetwhoConnector.Core.Validation;

namespace RetwhoConnector.Core.Services;

public sealed class ConnectorCoordinator : IAsyncDisposable
{
    private readonly ISecureSettingsService _settingsService;
    private readonly IPosAuthenticationService _authentication;
    private readonly IPosDataService _dataService;
    private readonly IBridgeSocketClient _bridge;
    private readonly IActionExecutionRegistry _registry;
    private readonly BridgeOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ConnectorCoordinator> _logger;
    private readonly CancellationToken _applicationToken;
    private readonly SemaphoreSlim _posSessionGate = new(1, 1);

    public ConnectorCoordinator(
        ISecureSettingsService settingsService,
        IPosAuthenticationService authentication,
        IPosDataService dataService,
        IBridgeSocketClient bridge,
        IActionExecutionRegistry registry,
        BridgeOptions options,
        TimeProvider timeProvider,
        ILogger<ConnectorCoordinator> logger,
        CancellationToken applicationToken)
    {
        _settingsService = settingsService;
        _authentication = authentication;
        _dataService = dataService;
        _bridge = bridge;
        _registry = registry;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
        _applicationToken = applicationToken;
        _bridge.ActionReceived += HandleActionAsync;
        _bridge.StateChanged += OnBridgeStateChanged;
        _bridge.SessionReplaced += OnSessionReplaced;
    }

    public ConnectorStatus CurrentStatus { get; private set; } = new();
    public event EventHandler<ConnectorStatus>? StatusChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ConnectorSettings? settings =
            await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (settings is null)
        {
            UpdateStatus(new ConnectorStatus());
            return;
        }

        UpdateStatus(CurrentStatus with
        {
            PosConfiguration = PosConfigurationState.Configured,
            PosAuthentication = settings.PosCookie is null
                ? PosAuthenticationState.NotConfigured
                : PosAuthenticationState.CachedSessionUnverified,
            Message = settings.PosCookie is null
                ? "POS login is required."
                : "Saved POS session available (not yet verified).",
        });
        if (settings.AutoConnect)
        {
            await ConnectWithSettingsAsync(settings, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task SaveAndConnectAsync(
        ConnectorSettings settings,
        CancellationToken cancellationToken)
    {
        ConnectorSettings validated = ConnectorSettingsValidator.Validate(settings);
        await _settingsService.SaveAsync(validated, cancellationToken)
            .ConfigureAwait(false);
        ConnectorSettings saved =
            await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new SettingsException(
                "SETTINGS_CORRUPT",
                "The saved settings could not be reloaded.");
        await ConnectWithSettingsAsync(saved, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PosSession> TestPosLoginAsync(
        ConnectorSettings settings,
        CancellationToken cancellationToken)
    {
        ConnectorSettings validated = ConnectorSettingsValidator.Validate(settings);
        UpdateStatus(CurrentStatus with
        {
            PosConfiguration = PosConfigurationState.Configured,
            PosAuthentication = PosAuthenticationState.Authenticating,
            Message = "Testing POS login…",
        });
        PosSession session = await _authentication.LoginAsync(
            validated,
            cancellationToken).ConfigureAwait(false);
        await _settingsService.SaveAsync(
            validated with { PosCookie = session.Cookie },
            cancellationToken).ConfigureAwait(false);
        UpdateStatus(CurrentStatus with
        {
            PosAuthentication = PosAuthenticationState.Authenticated,
            Message = "POS login succeeded.",
        });
        return session;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await _bridge.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        UpdateStatus(CurrentStatus with
        {
            BridgeTransport = BridgeTransportState.Disconnected,
            AgentRegistration = AgentRegistrationState.NotRegistered,
            Message = "Disconnected.",
        });
    }

    public async Task ClearSettingsAsync(CancellationToken cancellationToken)
    {
        await DisconnectAsync(cancellationToken).ConfigureAwait(false);
        await _settingsService.ClearAsync(cancellationToken).ConfigureAwait(false);
        UpdateStatus(new ConnectorStatus());
    }

    public async Task HandleActionAsync(
        BridgeActionContext context,
        CancellationToken cancellationToken)
    {
        BridgeAcknowledgement acknowledgement;
        if (!_bridge.IsRegistered)
        {
            acknowledgement = BridgeAcknowledgement.Failure(
                "NOT_REGISTERED: The connector is not registered.");
        }
        else
        {
            try
            {
                BridgeActionValidator.Validate(context.Action);
                acknowledgement = await _registry.ExecuteAsync(
                    context.Action.ActionId,
                    sharedToken => ExecuteActionAsync(
                        context.Action,
                        context.SessionCancellationToken,
                        sharedToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                acknowledgement = MapFailure(exception);
            }
        }

        await context.AcknowledgeOnceAsync(
            acknowledgement,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task ConnectWithSettingsAsync(
        ConnectorSettings settings,
        CancellationToken cancellationToken)
    {
        ConnectorSettings active = settings;
        if (string.IsNullOrWhiteSpace(active.PosCookie))
        {
            PosSession session = await TestPosLoginAsync(
                active,
                cancellationToken).ConfigureAwait(false);
            active = active with { PosCookie = session.Cookie };
        }

        await _bridge.ConnectAsync(active.LicenseKey, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<BridgeAcknowledgement> ExecuteActionAsync(
        BridgeAction action,
        CancellationToken sessionToken,
        CancellationToken sharedToken)
    {
        if (!action.Command.Equals(
            "get_current_data",
            StringComparison.Ordinal))
        {
            return BridgeAcknowledgement.Failure(
                "UNSUPPORTED_COMMAND: Only get_current_data is supported.");
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            _applicationToken,
            sessionToken,
            sharedToken);
        deadline.CancelAfter(_options.CommandDeadline);
        CancellationToken token = deadline.Token;
        var stopwatch = Stopwatch.StartNew();
        UpdateStatus(CurrentStatus with
        {
            LastCommand = LastCommandState.Running,
            LastCommandTimestamp = _timeProvider.GetUtcNow(),
            Message = "Retrieving fresh POS data…",
        });

        try
        {
            ConnectorSettings settings =
                await _settingsService.LoadAsync(token).ConfigureAwait(false)
                ?? throw new SettingsException(
                    "SETTINGS_MISSING",
                    "Connector settings are not available.");
            await _posSessionGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                VdatetimeResult result = await GetWithOneRefreshAsync(
                    settings,
                    token).ConfigureAwait(false);
                byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
                    result,
                    ConnectorJson.Options);
                if (payload.Length >= _options.MaximumPayloadBytes)
                {
                    return BridgeAcknowledgement.Failure(
                        "PAYLOAD_TOO_LARGE: The POS result exceeds the bridge limit.");
                }

                UpdateStatus(CurrentStatus with
                {
                    PosAuthentication = PosAuthenticationState.Authenticated,
                    LastCommand = LastCommandState.Completed,
                    LastCommandTimestamp = _timeProvider.GetUtcNow(),
                    Message = "Command completed.",
                });
                _logger.LogInformation(
                    "Command completed for action {ActionId} in {ElapsedMilliseconds} ms",
                    action.ActionId,
                    stopwatch.ElapsedMilliseconds);
                return BridgeAcknowledgement.Success(result);
            }
            finally
            {
                _posSessionGate.Release();
            }
        }
        catch (Exception exception)
        {
            UpdateStatus(CurrentStatus with
            {
                LastCommand = exception is OperationCanceledException
                    ? LastCommandState.Cancelled
                    : LastCommandState.Failed,
                LastCommandTimestamp = _timeProvider.GetUtcNow(),
                Message = MapFailure(exception).Error ?? "Command failed.",
            });
            return MapFailure(exception);
        }
    }

    private async Task<VdatetimeResult> GetWithOneRefreshAsync(
        ConnectorSettings settings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.PosCookie))
        {
            return await RefreshAndGetAsync(settings, cancellationToken)
                .ConfigureAwait(false);
        }

        try
        {
            return await _dataService.GetVdatetimeAsync(
                settings,
                settings.PosCookie,
                cancellationToken).ConfigureAwait(false);
        }
        catch (PosAuthenticationException)
        {
            return await RefreshAndGetAsync(settings, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<VdatetimeResult> RefreshAndGetAsync(
        ConnectorSettings settings,
        CancellationToken cancellationToken)
    {
        UpdateStatus(CurrentStatus with
        {
            PosAuthentication = PosAuthenticationState.RefreshingSession,
            Message = "Refreshing the POS session…",
        });
        PosSession session = await _authentication.LoginAsync(
            settings,
            cancellationToken).ConfigureAwait(false);
        ConnectorSettings updated = settings with { PosCookie = session.Cookie };
        await _settingsService.SaveAsync(updated, cancellationToken)
            .ConfigureAwait(false);
        return await _dataService.GetVdatetimeAsync(
            updated,
            session.Cookie,
            cancellationToken).ConfigureAwait(false);
    }

    private BridgeAcknowledgement MapFailure(Exception exception) =>
        exception switch
        {
            ConnectorException connector =>
                BridgeAcknowledgement.Failure(
                    $"{connector.Code}: {connector.SafeMessage}"),
            ArgumentException argument =>
                BridgeAcknowledgement.Failure(
                    $"INVALID_ACTION: {argument.Message}"),
            OperationCanceledException when _applicationToken.IsCancellationRequested =>
                BridgeAcknowledgement.Failure(
                    "COMMAND_CANCELLED: The connector is shutting down."),
            OperationCanceledException =>
                BridgeAcknowledgement.Failure(
                    "POS_TIMEOUT: The local POS did not respond before the deadline."),
            _ => BridgeAcknowledgement.Failure(
                "INTERNAL_ERROR: The connector could not complete the command."),
        };

    private void OnBridgeStateChanged(
        object? sender,
        BridgeConnectionStateChangedEventArgs args) =>
        UpdateStatus(CurrentStatus with
        {
            BridgeTransport = args.TransportState,
            AgentRegistration = args.RegistrationState,
            Message = args.Message,
        });

    private void OnSessionReplaced(object? sender, EventArgs args) =>
        UpdateStatus(CurrentStatus with
        {
            BridgeTransport = BridgeTransportState.SessionReplaced,
            AgentRegistration = AgentRegistrationState.SessionReplaced,
            Message = "This connector session was replaced.",
        });

    private void UpdateStatus(ConnectorStatus status)
    {
        CurrentStatus = status;
        StatusChanged?.Invoke(this, status);
    }

    public async ValueTask DisposeAsync()
    {
        _bridge.ActionReceived -= HandleActionAsync;
        _bridge.StateChanged -= OnBridgeStateChanged;
        _bridge.SessionReplaced -= OnSessionReplaced;
        _posSessionGate.Dispose();
        await _bridge.DisposeAsync().ConfigureAwait(false);
    }
}
