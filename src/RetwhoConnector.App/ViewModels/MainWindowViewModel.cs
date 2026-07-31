using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetwhoConnector.App.Services;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Exceptions;
using RetwhoConnector.Core.Models;
using RetwhoConnector.Core.Services;
using WpfApplication = System.Windows.Application;

namespace RetwhoConnector.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IAgentOrchestrationService _orchestration;
    private readonly IAgentLog _agentLog;
    private readonly UiLogBufferSink _uiLogBuffer;
    private readonly IConfigurationDialogService _configurationDialog;
    private readonly IApplicationControlService _applicationControl;
    private readonly IUserDialogService _dialogs;
    private bool _isBusy;
    private string _configurationStatus = "Missing configuration";
    private string _serverStatus = "Offline";
    private string _agentStatus = "Idle";
    private string _loggingStatus = "Stopped";
    private string _connectionActionText = "Connect";
    private string _bannerMessage =
        "Open Settings to configure the local POS and license.";
    private int _logRefreshScheduled;

    public MainWindowViewModel(
        IAgentOrchestrationService orchestration,
        IAgentLog agentLog,
        UiLogBufferSink uiLogBuffer,
        IConfigurationDialogService configurationDialog,
        IApplicationControlService applicationControl,
        IUserDialogService dialogs)
    {
        _orchestration = orchestration;
        _agentLog = agentLog;
        _uiLogBuffer = uiLogBuffer;
        _configurationDialog = configurationDialog;
        _applicationControl = applicationControl;
        _dialogs = dialogs;

        OpenSettingsCommand = new AsyncRelayCommand(
            () => ExecuteAsync(OpenSettingsAsync),
            () => !IsBusy);
        ToggleConnectionCommand = new AsyncRelayCommand(
            () => ExecuteAsync(ToggleConnectionAsync),
            () => !IsBusy);
        OpenLogsFolderCommand = new RelayCommand(
            OpenLogsFolder,
            () => !IsBusy);
        ExitCommand = new AsyncRelayCommand(
            _applicationControl.RequestExitAsync);

        _orchestration.StatusChanged += OnStatusChanged;
        _agentLog.HealthChanged += OnLoggingHealthChanged;
        _uiLogBuffer.Changed += OnUiLogBufferChanged;
    }

    public ObservableCollection<LogEntryViewModel> ActivityEntries { get; } = [];

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OpenSettingsCommand.NotifyCanExecuteChanged();
                ToggleConnectionCommand.NotifyCanExecuteChanged();
                OpenLogsFolderCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ConfigurationStatus
    {
        get => _configurationStatus;
        private set => SetProperty(ref _configurationStatus, value);
    }

    public string ServerStatus
    {
        get => _serverStatus;
        private set => SetProperty(ref _serverStatus, value);
    }

    public string AgentStatus
    {
        get => _agentStatus;
        private set => SetProperty(ref _agentStatus, value);
    }

    public string LoggingStatus
    {
        get => _loggingStatus;
        private set => SetProperty(ref _loggingStatus, value);
    }

    public string ConnectionActionText
    {
        get => _connectionActionText;
        private set => SetProperty(ref _connectionActionText, value);
    }

    public string BannerMessage
    {
        get => _bannerMessage;
        private set => SetProperty(ref _bannerMessage, value);
    }

    public IAsyncRelayCommand OpenSettingsCommand { get; }
    public IAsyncRelayCommand ToggleConnectionCommand { get; }
    public IRelayCommand OpenLogsFolderCommand { get; }
    public IAsyncRelayCommand ExitCommand { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _orchestration.InitializeAsync(cancellationToken);
        }
        catch (ConnectorException exception)
        {
            ReportSafeFailure(exception.Code, exception.SafeMessage);
        }
        catch (Exception exception)
        {
            _agentLog.TryWrite(
                AgentLogLevel.Error,
                AgentLogCategory.Error,
                "The dashboard could not initialize.",
                exception.GetType().FullName);
            BannerMessage =
                "The dashboard could not initialize. See the local logs.";
        }

        ApplyStatus(_orchestration.CurrentStatus);
        ApplyLoggingHealth(_agentLog.CurrentHealth);
        RefreshActivityEntries();
    }

    private async Task OpenSettingsAsync(CancellationToken cancellationToken)
    {
        await _configurationDialog.ShowAsync(cancellationToken);
        ApplyStatus(_orchestration.CurrentStatus);
    }

    private Task ToggleConnectionAsync(CancellationToken cancellationToken)
    {
        BridgeTransportState transport =
            _orchestration.CurrentStatus.BridgeTransport;
        return transport is
            BridgeTransportState.Connected or
            BridgeTransportState.Connecting or
            BridgeTransportState.Reconnecting
                ? _orchestration.DisconnectAsync(cancellationToken)
                : _orchestration.ConnectSavedAsync(cancellationToken);
    }

    private void OpenLogsFolder()
    {
        try
        {
            _applicationControl.OpenLogsFolder();
        }
        catch (Exception exception)
        {
            _agentLog.TryWrite(
                AgentLogLevel.Error,
                AgentLogCategory.Error,
                "The logs folder could not be opened.",
                exception.GetType().FullName);
            _dialogs.ShowError(
                "The logs folder could not be opened.");
        }
    }

    private async Task ExecuteAsync(
        Func<CancellationToken, Task> operation)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await operation(CancellationToken.None);
        }
        catch (ConnectorException exception)
        {
            ReportSafeFailure(exception.Code, exception.SafeMessage);
        }
        catch (ArgumentException exception)
        {
            ReportSafeFailure("CONFIG_INVALID", exception.Message);
        }
        catch (Exception exception)
        {
            _agentLog.TryWrite(
                AgentLogLevel.Error,
                AgentLogCategory.Error,
                "The requested operation failed.",
                exception.GetType().FullName);
            BannerMessage =
                "The operation failed. See the local logs for details.";
            _dialogs.ShowError(BannerMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ReportSafeFailure(string code, string safeMessage)
    {
        BannerMessage = $"{code}: {safeMessage}";
        _agentLog.TryWrite(
            AgentLogLevel.Error,
            AgentLogCategory.Error,
            BannerMessage);
        _dialogs.ShowError(BannerMessage);
    }

    private void OnStatusChanged(object? sender, ConnectorStatus status) =>
        RunOnUiThread(() => ApplyStatus(status));

    private void OnLoggingHealthChanged(
        object? sender,
        LogPipelineHealth health) =>
        RunOnUiThread(() => ApplyLoggingHealth(health));

    private void OnUiLogBufferChanged(object? sender, EventArgs args)
    {
        if (Interlocked.Exchange(ref _logRefreshScheduled, 1) != 0)
        {
            return;
        }

        RunOnUiThread(() =>
        {
            try
            {
                RefreshActivityEntries();
            }
            finally
            {
                Interlocked.Exchange(ref _logRefreshScheduled, 0);
            }
        });
    }

    private void ApplyStatus(ConnectorStatus status)
    {
        ConfigurationStatus = status.PosConfiguration switch
        {
            PosConfigurationState.Configured => "Configured",
            PosConfigurationState.Invalid => "Invalid",
            _ => "Missing configuration",
        };
        ServerStatus = status.BridgeTransport switch
        {
            BridgeTransportState.Connected => "Connected",
            BridgeTransportState.Connecting => "Connecting",
            BridgeTransportState.Reconnecting => "Reconnecting",
            BridgeTransportState.AuthenticationFailed =>
                "Authentication failed",
            BridgeTransportState.SessionReplaced => "Session replaced",
            BridgeTransportState.Stopping => "Disconnecting",
            _ => "Offline",
        };
        AgentStatus = status.AgentRegistration switch
        {
            AgentRegistrationState.Registered => "Active",
            AgentRegistrationState.Failed => "Error",
            AgentRegistrationState.SessionReplaced => "Inactive",
            _ => "Idle",
        };
        ConnectionActionText = status.BridgeTransport is
            BridgeTransportState.Connected or
            BridgeTransportState.Connecting or
            BridgeTransportState.Reconnecting
                ? "Disconnect"
                : "Connect";
        BannerMessage = status.Message;
    }

    private void ApplyLoggingHealth(LogPipelineHealth health)
    {
        LoggingStatus = health.State switch
        {
            LoggingHealthState.Healthy => "Healthy",
            LoggingHealthState.Degraded when health.DroppedEntries > 0 =>
                $"Degraded ({health.DroppedEntries} dropped)",
            LoggingHealthState.Degraded => "Degraded",
            _ => "Stopped",
        };
    }

    private void RefreshActivityEntries()
    {
        IReadOnlyList<LogEntry> snapshot = _uiLogBuffer.GetSnapshot();
        ActivityEntries.Clear();
        foreach (LogEntry entry in snapshot)
        {
            ActivityEntries.Add(new LogEntryViewModel(entry));
        }
    }

    private static void RunOnUiThread(Action action)
    {
        WpfApplication? application = WpfApplication.Current;
        if (application is null ||
            application.Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _ = application.Dispatcher.InvokeAsync(action);
    }
}
