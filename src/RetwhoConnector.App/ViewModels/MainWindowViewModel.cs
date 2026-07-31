using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetwhoConnector.App.Services;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Exceptions;
using RetwhoConnector.Core.Models;
using RetwhoConnector.Core.Serialization;
using RetwhoConnector.Core.Services;
using RetwhoConnector.Core.Validation;

namespace RetwhoConnector.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly ConnectorCoordinator _coordinator;
    private readonly IAgentOrchestrationService _orchestration;
    private readonly ISecureSettingsService _settingsService;
    private readonly ICertificateTrustService _certificateTrust;
    private readonly IUserDialogService _dialogs;
    private string? _pinnedCertificate;
    private string _licenseKey = string.Empty;
    private string _posBaseUrl = string.Empty;
    private string _posUsername = string.Empty;
    private string _posPassword = string.Empty;
    private bool _autoConnect = true;
    private bool _isBusy;
    private string _validationMessage = string.Empty;
    private string _posConfigurationStatus = "Not configured";
    private string _posAuthenticationStatus = "Not configured";
    private string _bridgeStatus = "Disconnected";
    private string _registrationStatus = "Not registered";
    private string _lastCommandStatus = "None";
    private string _lastJsonResult = "{}";

    public MainWindowViewModel(
        ConnectorCoordinator coordinator,
        IAgentOrchestrationService orchestration,
        ISecureSettingsService settingsService,
        ICertificateTrustService certificateTrust,
        IUserDialogService dialogs)
    {
        _coordinator = coordinator;
        _orchestration = orchestration;
        _settingsService = settingsService;
        _certificateTrust = certificateTrust;
        _dialogs = dialogs;
        SaveAndConnectCommand = new AsyncRelayCommand(
            () => ExecuteAsync(SaveAndConnectAsync),
            () => !IsBusy);
        DisconnectCommand = new AsyncRelayCommand(
            () => ExecuteAsync(_coordinator.DisconnectAsync),
            () => !IsBusy);
        TestPosLoginCommand = new AsyncRelayCommand(
            () => ExecuteAsync(TestPosLoginAsync),
            () => !IsBusy);
        TrustPosCertificateCommand = new AsyncRelayCommand(
            () => ExecuteAsync(TrustCertificateAsync),
            () => !IsBusy);
        ClearSavedSettingsCommand = new AsyncRelayCommand(
            () => ExecuteAsync(ClearSettingsAsync),
            () => !IsBusy);
        _coordinator.StatusChanged += OnStatusChanged;
        _coordinator.ResultReceived += OnResultReceived;
    }

    public string LicenseKey
    {
        get => _licenseKey;
        set => SetProperty(ref _licenseKey, value);
    }

    public string PosBaseUrl
    {
        get => _posBaseUrl;
        set => SetProperty(ref _posBaseUrl, value);
    }

    public string PosUsername
    {
        get => _posUsername;
        set => SetProperty(ref _posUsername, value);
    }

    public string PosPassword
    {
        get => _posPassword;
        set => SetProperty(ref _posPassword, value);
    }

    public bool AutoConnect
    {
        get => _autoConnect;
        set => SetProperty(ref _autoConnect, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifyCommands();
            }
        }
    }

    public string ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }

    public string PosConfigurationStatus
    {
        get => _posConfigurationStatus;
        private set => SetProperty(ref _posConfigurationStatus, value);
    }

    public string PosAuthenticationStatus
    {
        get => _posAuthenticationStatus;
        private set => SetProperty(ref _posAuthenticationStatus, value);
    }

    public string BridgeStatus
    {
        get => _bridgeStatus;
        private set => SetProperty(ref _bridgeStatus, value);
    }

    public string RegistrationStatus
    {
        get => _registrationStatus;
        private set => SetProperty(ref _registrationStatus, value);
    }

    public string LastCommandStatus
    {
        get => _lastCommandStatus;
        private set => SetProperty(ref _lastCommandStatus, value);
    }

    public string LastJsonResult
    {
        get => _lastJsonResult;
        private set => SetProperty(ref _lastJsonResult, value);
    }

    public ObservableCollection<string> ActivityItems { get; } = [];
    public IAsyncRelayCommand SaveAndConnectCommand { get; }
    public IAsyncRelayCommand DisconnectCommand { get; }
    public IAsyncRelayCommand TestPosLoginCommand { get; }
    public IAsyncRelayCommand TrustPosCertificateCommand { get; }
    public IAsyncRelayCommand ClearSavedSettingsCommand { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            ConnectorSettings? settings =
                await _settingsService.LoadAsync(cancellationToken);
            if (settings is not null)
            {
                LicenseKey = settings.LicenseKey;
                PosBaseUrl = settings.PosBaseUrl;
                PosUsername = settings.PosUsername;
                PosPassword = settings.PosPassword;
                AutoConnect = settings.AutoConnect;
                _pinnedCertificate = settings.PinnedCertificateSha256;
            }

            await _orchestration.InitializeAsync(cancellationToken);
        }
        catch (ConnectorException exception)
        {
            ValidationMessage = $"{exception.Code}: {exception.SafeMessage}";
            AddActivity(ValidationMessage);
        }
    }

    private async Task SaveAndConnectAsync(CancellationToken cancellationToken)
    {
        ConnectorSettings settings = BuildSettings();
        await _coordinator.SaveAndConnectAsync(settings, cancellationToken);
        AddActivity("Settings saved and connector registered.");
    }

    private async Task TestPosLoginAsync(CancellationToken cancellationToken)
    {
        await _coordinator.TestPosLoginAsync(BuildSettings(), cancellationToken);
        AddActivity("POS login test succeeded.");
    }

    private async Task TrustCertificateAsync(CancellationToken cancellationToken)
    {
        ConnectorSettings settings = BuildSettings();
        PresentedCertificate certificate = await _certificateTrust.InspectAsync(
            new Uri(settings.PosBaseUrl),
            cancellationToken);
        if (!_dialogs.ConfirmCertificate(certificate))
        {
            AddActivity("POS certificate approval cancelled.");
            return;
        }

        _pinnedCertificate = certificate.Sha256Fingerprint;
        await _settingsService.SaveAsync(
            settings with
            {
                PinnedCertificateSha256 = certificate.Sha256Fingerprint,
            },
            cancellationToken);
        AddActivity("POS certificate fingerprint approved and encrypted.");
    }

    private async Task ClearSettingsAsync(CancellationToken cancellationToken)
    {
        if (!_dialogs.ConfirmClearSettings())
        {
            return;
        }

        await _coordinator.ClearSettingsAsync(cancellationToken);
        LicenseKey = string.Empty;
        PosBaseUrl = string.Empty;
        PosUsername = string.Empty;
        PosPassword = string.Empty;
        _pinnedCertificate = null;
        LastJsonResult = "{}";
        AddActivity("Encrypted settings cleared.");
    }

    private ConnectorSettings BuildSettings() =>
        ConnectorSettingsValidator.Validate(new ConnectorSettings
        {
            LicenseKey = LicenseKey,
            PosBaseUrl = PosBaseUrl,
            PosUsername = PosUsername,
            PosPassword = PosPassword,
            PinnedCertificateSha256 = _pinnedCertificate,
            AutoConnect = AutoConnect,
        });

    private async Task ExecuteAsync(
        Func<CancellationToken, Task> operation)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ValidationMessage = string.Empty;
        try
        {
            await operation(CancellationToken.None);
        }
        catch (ConnectorException exception)
        {
            ValidationMessage = $"{exception.Code}: {exception.SafeMessage}";
            AddActivity(ValidationMessage);
            _dialogs.ShowError(ValidationMessage);
        }
        catch (ArgumentException exception)
        {
            ValidationMessage = exception.Message;
            AddActivity("Configuration validation failed.");
            _dialogs.ShowError(ValidationMessage);
        }
        catch (Exception)
        {
            ValidationMessage =
                "The operation failed. See the local connector log for details.";
            AddActivity(ValidationMessage);
            _dialogs.ShowError(ValidationMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnStatusChanged(object? sender, ConnectorStatus status)
    {
        void Apply()
        {
            PosConfigurationStatus = status.PosConfiguration.ToString();
            PosAuthenticationStatus = status.PosAuthentication.ToString();
            BridgeStatus = status.BridgeTransport.ToString();
            RegistrationStatus = status.AgentRegistration.ToString();
            LastCommandStatus = status.LastCommand.ToString();
            AddActivity(status.Message);
        }

        System.Windows.Application.Current.Dispatcher.Invoke(Apply);
    }

    private void OnResultReceived(object? sender, VdatetimeResult result)
    {
        void Apply() =>
            LastJsonResult = JsonSerializer.Serialize(
                result,
                new JsonSerializerOptions(ConnectorJson.Options)
                {
                    WriteIndented = true,
                });
        System.Windows.Application.Current.Dispatcher.Invoke(Apply);
    }

    private void AddActivity(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        ActivityItems.Insert(
            0,
            $"{DateTimeOffset.Now:HH:mm:ss} {message}");
        while (ActivityItems.Count > 200)
        {
            ActivityItems.RemoveAt(ActivityItems.Count - 1);
        }
    }

    private void NotifyCommands()
    {
        SaveAndConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
        TestPosLoginCommand.NotifyCanExecuteChanged();
        TrustPosCertificateCommand.NotifyCanExecuteChanged();
        ClearSavedSettingsCommand.NotifyCanExecuteChanged();
    }
}
