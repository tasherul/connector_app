using Microsoft.Extensions.Hosting;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Exceptions;
using RetwhoConnector.Core.Models;
using RetwhoConnector.Core.Validation;

namespace RetwhoConnector.Core.Services;

public sealed class AgentOrchestrationService :
    BackgroundService,
    IAgentOrchestrationService
{
    private readonly ConnectorCoordinator _coordinator;
    private readonly ISecureSettingsService _settingsService;
    private readonly IPosAuthenticationService _authentication;
    private readonly ICertificateTrustService _certificateTrust;
    private readonly IAgentLog _agentLog;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private bool _initialized;
    private int _disposed;

    public AgentOrchestrationService(
        ConnectorCoordinator coordinator,
        ISecureSettingsService settingsService,
        IPosAuthenticationService authentication,
        ICertificateTrustService certificateTrust,
        IAgentLog agentLog)
    {
        _coordinator = coordinator;
        _settingsService = settingsService;
        _authentication = authentication;
        _certificateTrust = certificateTrust;
        _agentLog = agentLog;
        _coordinator.StatusChanged += OnCoordinatorStatusChanged;
        _coordinator.ResultReceived += OnCoordinatorResultReceived;
    }

    public ConnectorStatus CurrentStatus => _coordinator.CurrentStatus;
    public ConnectorSettings? CurrentSettings { get; private set; }

    public event EventHandler<ConnectorStatus>? StatusChanged;
    public event EventHandler<VdatetimeResult>? ResultReceived;

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        try
        {
            await InitializeAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (ConnectorException exception)
        {
            _agentLog.TryWrite(
                AgentLogLevel.Error,
                AgentLogCategory.Error,
                $"{exception.Code}: {exception.SafeMessage}");
        }
        catch (Exception exception)
        {
            _agentLog.TryWrite(
                AgentLogLevel.Critical,
                AgentLogCategory.Error,
                "Agent initialization failed.",
                exception.GetType().FullName);
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _initializationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            CurrentSettings = await _settingsService
                .LoadAsync(cancellationToken)
                .ConfigureAwait(false);
            await _coordinator.InitializeAsync(cancellationToken)
                .ConfigureAwait(false);
            _initialized = true;
            _agentLog.TryWrite(
                AgentLogLevel.Information,
                AgentLogCategory.General,
                CurrentSettings is null
                    ? "Agent configuration is required."
                    : "Encrypted agent settings loaded.");
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public async Task<ConnectorSettings?> LoadSettingsAsync(
        CancellationToken cancellationToken)
    {
        CurrentSettings = await _settingsService
            .LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        return CurrentSettings;
    }

    public async Task SaveTestAndConnectAsync(
        ConnectorSettings settings,
        CancellationToken cancellationToken)
    {
        ConnectorSettings validated =
            ConnectorSettingsValidator.Validate(settings);
        _agentLog.TryWrite(
            AgentLogLevel.Information,
            AgentLogCategory.Session,
            "Testing POS authentication.");
        PosSession session = await _authentication
            .LoginAsync(validated, cancellationToken)
            .ConfigureAwait(false);
        ConnectorSettings authenticated = validated with
        {
            PosCookie = session.Cookie,
        };
        await _settingsService.SaveAsync(
            authenticated,
            cancellationToken).ConfigureAwait(false);
        CurrentSettings = await _settingsService
            .LoadAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new SettingsException(
                "SETTINGS_CORRUPT",
                "The saved settings could not be reloaded.");
        _agentLog.TryWrite(
            AgentLogLevel.Information,
            AgentLogCategory.Success,
            "POS authentication succeeded and settings were encrypted.");

        await _coordinator.SaveAndConnectAsync(
            CurrentSettings,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ConnectSavedAsync(
        CancellationToken cancellationToken)
    {
        ConnectorSettings settings =
            await LoadSettingsAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new SettingsException(
                "SETTINGS_MISSING",
                "Connector settings are not available.");
        await _coordinator.SaveAndConnectAsync(
            settings,
            cancellationToken).ConfigureAwait(false);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken) =>
        _coordinator.DisconnectAsync(cancellationToken);

    public Task<PresentedCertificate> InspectCertificateAsync(
        string posBaseUrl,
        CancellationToken cancellationToken)
    {
        Uri origin =
            ConnectorSettingsValidator.ValidatePosOrigin(posBaseUrl);
        return _certificateTrust.InspectAsync(origin, cancellationToken);
    }

    public async Task ClearSettingsAsync(
        CancellationToken cancellationToken)
    {
        await _coordinator.ClearSettingsAsync(cancellationToken)
            .ConfigureAwait(false);
        CurrentSettings = null;
        _agentLog.TryWrite(
            AgentLogLevel.Information,
            AgentLogCategory.General,
            "Encrypted agent settings were cleared.");
    }

    private void OnCoordinatorStatusChanged(
        object? sender,
        ConnectorStatus status)
    {
        _agentLog.TryWrite(
            MapLevel(status),
            MapCategory(status),
            status.Message);
        StatusChanged?.Invoke(this, status);
    }

    private void OnCoordinatorResultReceived(
        object? sender,
        VdatetimeResult result) =>
        ResultReceived?.Invoke(this, result);

    private static AgentLogLevel MapLevel(ConnectorStatus status) =>
        status.LastCommand == LastCommandState.Failed ||
        status.BridgeTransport == BridgeTransportState.AuthenticationFailed ||
        status.AgentRegistration == AgentRegistrationState.Failed
            ? AgentLogLevel.Error
            : AgentLogLevel.Information;

    private static AgentLogCategory MapCategory(ConnectorStatus status)
    {
        if (status.LastCommand == LastCommandState.Failed ||
            status.BridgeTransport is
                BridgeTransportState.AuthenticationFailed or
                BridgeTransportState.SessionReplaced ||
            status.AgentRegistration is
                AgentRegistrationState.Failed or
                AgentRegistrationState.SessionReplaced)
        {
            return AgentLogCategory.Error;
        }

        if (status.PosAuthentication is
            PosAuthenticationState.Authenticating or
            PosAuthenticationState.CachedSessionUnverified or
            PosAuthenticationState.RefreshingSession)
        {
            return AgentLogCategory.Session;
        }

        if (status.LastCommand is
            LastCommandState.Running or
            LastCommandState.Completed)
        {
            return AgentLogCategory.Action;
        }

        return status.AgentRegistration == AgentRegistrationState.Registered
            ? AgentLogCategory.Success
            : AgentLogCategory.General;
    }

    public override void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _coordinator.StatusChanged -= OnCoordinatorStatusChanged;
        _coordinator.ResultReceived -= OnCoordinatorResultReceived;
        _initializationGate.Dispose();
        base.Dispose();
    }
}
