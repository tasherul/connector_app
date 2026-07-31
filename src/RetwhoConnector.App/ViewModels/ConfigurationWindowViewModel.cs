using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetwhoConnector.App.Services;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Exceptions;
using RetwhoConnector.Core.Models;
using RetwhoConnector.Core.Validation;

namespace RetwhoConnector.App.ViewModels;

public sealed class ConfigurationWindowViewModel : ObservableObject
{
    private readonly IAgentOrchestrationService _orchestration;
    private readonly IUserDialogService _dialogs;
    private string _licenseKey = string.Empty;
    private string _posBaseUrl = string.Empty;
    private string _posUsername = string.Empty;
    private string _posPassword = string.Empty;
    private string? _pinnedCertificate;
    private string? _loadedOrigin;
    private bool _autoConnect = true;
    private bool _isBusy;
    private string _validationMessage = string.Empty;

    public ConfigurationWindowViewModel(
        IAgentOrchestrationService orchestration,
        IUserDialogService dialogs)
    {
        _orchestration = orchestration;
        _dialogs = dialogs;
        SaveCommand = new AsyncRelayCommand(
            SaveAsync,
            () => !IsBusy);
        ClearCommand = new AsyncRelayCommand(
            ClearAsync,
            () => !IsBusy);
        CancelCommand = new RelayCommand(
            () => CloseRequested?.Invoke(this, false),
            () => !IsBusy);
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
                SaveCommand.NotifyCanExecuteChanged();
                ClearCommand.NotifyCanExecuteChanged();
                CancelCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }

    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand ClearCommand { get; }
    public IRelayCommand CancelCommand { get; }

    public event EventHandler<bool?>? CloseRequested;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        ConnectorSettings? settings =
            _orchestration.CurrentSettings ??
            await _orchestration.LoadSettingsAsync(cancellationToken);
        if (settings is null)
        {
            return;
        }

        LicenseKey = settings.LicenseKey;
        PosBaseUrl = settings.PosBaseUrl;
        PosUsername = settings.PosUsername;
        PosPassword = settings.PosPassword;
        AutoConnect = settings.AutoConnect;
        _pinnedCertificate = settings.PinnedCertificateSha256;
        _loadedOrigin = ConnectorSettingsValidator
            .ValidatePosOrigin(settings.PosBaseUrl)
            .GetLeftPart(UriPartial.Authority);
    }

    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ValidationMessage = string.Empty;
        try
        {
            ConnectorSettings draft = BuildSettings();
            PresentedCertificate certificate =
                await _orchestration.InspectCertificateAsync(
                    draft.PosBaseUrl,
                    CancellationToken.None);
            string normalizedOrigin =
                ConnectorSettingsValidator
                    .ValidatePosOrigin(draft.PosBaseUrl)
                    .GetLeftPart(UriPartial.Authority);
            bool originChanged = !string.Equals(
                _loadedOrigin,
                normalizedOrigin,
                StringComparison.OrdinalIgnoreCase);
            string? approvedPin = originChanged
                ? null
                : _pinnedCertificate;
            if (!certificate.IsSystemTrusted &&
                !string.Equals(
                    approvedPin,
                    certificate.Sha256Fingerprint,
                    StringComparison.Ordinal))
            {
                if (!_dialogs.ConfirmCertificate(certificate))
                {
                    ValidationMessage =
                        "Certificate approval was cancelled.";
                    return;
                }

                approvedPin = certificate.Sha256Fingerprint;
            }

            await _orchestration.SaveTestAndConnectAsync(
                draft with
                {
                    PinnedCertificateSha256 =
                        certificate.IsSystemTrusted ? null : approvedPin,
                },
                CancellationToken.None);
            CloseRequested?.Invoke(this, true);
        }
        catch (ConnectorException exception)
        {
            ValidationMessage =
                $"{exception.Code}: {exception.SafeMessage}";
            _dialogs.ShowError(ValidationMessage);
        }
        catch (ArgumentException exception)
        {
            ValidationMessage = exception.Message;
        }
        catch (Exception)
        {
            ValidationMessage =
                _orchestration.CurrentSettings is null
                    ? "The connection test failed. Existing settings were not changed."
                    : "POS settings were saved, but the cloud connection failed.";
            _dialogs.ShowError(ValidationMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ClearAsync()
    {
        if (!_dialogs.ConfirmClearSettings())
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _orchestration.ClearSettingsAsync(
                CancellationToken.None);
            LicenseKey = string.Empty;
            PosBaseUrl = string.Empty;
            PosUsername = string.Empty;
            PosPassword = string.Empty;
            _pinnedCertificate = null;
            _loadedOrigin = null;
            ValidationMessage = "Encrypted settings were cleared.";
        }
        catch (ConnectorException exception)
        {
            ValidationMessage =
                $"{exception.Code}: {exception.SafeMessage}";
            _dialogs.ShowError(ValidationMessage);
        }
        finally
        {
            IsBusy = false;
        }
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
}
