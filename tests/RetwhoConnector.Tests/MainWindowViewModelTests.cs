using System.Net.Security;
using RetwhoConnector.App.Services;
using RetwhoConnector.App.ViewModels;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Models;
using RetwhoConnector.Core.Services;

namespace RetwhoConnector.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task LoggingHealthEvent_DoesNotOverwriteOperationBanner()
    {
        var orchestration = new FakeOrchestration();
        var log = new FakeAgentLog();
        var configurationDialog = new FakeConfigurationDialog
        {
            Exception = new InvalidOperationException("Synthetic dialog failure."),
        };
        var viewModel = new MainWindowViewModel(
            orchestration,
            log,
            new UiLogBufferSink(),
            configurationDialog,
            new FakeApplicationControl(),
            new FakeDialogs());

        await viewModel.InitializeAsync(CancellationToken.None);
        await viewModel.OpenSettingsCommand.ExecuteAsync(null);
        string operationBanner = viewModel.BannerMessage;

        log.RaiseHealth(new LogPipelineHealth(
            LoggingHealthState.Degraded,
            1,
            "Logging pipeline degraded."));

        Assert.Equal("The operation failed. See the local logs for details.", operationBanner);
        Assert.Equal(operationBanner, viewModel.BannerMessage);
        Assert.Equal("Degraded (1 dropped)", viewModel.LoggingIndicator.Status);
    }

    private sealed class FakeOrchestration : IAgentOrchestrationService
    {
        public ConnectorStatus CurrentStatus { get; private set; } = new()
        {
            PosConfiguration = PosConfigurationState.Configured,
            BridgeTransport = BridgeTransportState.Connected,
            AgentRegistration = AgentRegistrationState.Registered,
            Message = "Connected.",
        };

        public ConnectorSettings? CurrentSettings => null;
        public event EventHandler<ConnectorStatus>? StatusChanged;
#pragma warning disable CS0067
        public event EventHandler<VdatetimeResult>? ResultReceived;
#pragma warning restore CS0067
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ConnectorSettings?> LoadSettingsAsync(CancellationToken cancellationToken) => Task.FromResult<ConnectorSettings?>(null);
        public Task SaveTestAndConnectAsync(ConnectorSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ConnectSavedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<PresentedCertificate> InspectCertificateAsync(string posBaseUrl, CancellationToken cancellationToken) => Task.FromResult(new PresentedCertificate { Subject = "CN=fake", Issuer = "CN=fake", Sha256Fingerprint = "00", PolicyErrors = SslPolicyErrors.None, ValidFromUtc = DateTimeOffset.UnixEpoch, ValidToUtc = DateTimeOffset.UnixEpoch });
        public Task ClearSettingsAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void RaiseStatus(string message)
        {
            CurrentStatus = CurrentStatus with { Message = message };
            StatusChanged?.Invoke(this, CurrentStatus);
        }
    }

    private sealed class FakeAgentLog : IAgentLog
    {
        public LogPipelineHealth CurrentHealth { get; private set; } = new(LoggingHealthState.Healthy, 0, "Healthy.");
        public event EventHandler<LogPipelineHealth>? HealthChanged;
        public bool TryWrite(AgentLogLevel level, AgentLogCategory category, string message, string? details = null, string? correlationId = null) => true;

        public void RaiseHealth(LogPipelineHealth health)
        {
            CurrentHealth = health;
            HealthChanged?.Invoke(this, health);
        }
    }

    private sealed class FakeConfigurationDialog : IConfigurationDialogService
    {
        public Exception? Exception { get; init; }

        public Task ShowAsync(CancellationToken cancellationToken) =>
            Exception is null
                ? Task.CompletedTask
                : Task.FromException(Exception);
    }

    private sealed class FakeApplicationControl : IApplicationControlService
    {
        public bool IsExitRequested => false;
        public void OpenLogsFolder() { }
        public Task RequestExitAsync() => Task.CompletedTask;
    }

    private sealed class FakeDialogs : IUserDialogService
    {
        public bool ConfirmCertificate(PresentedCertificate certificate) => false;
        public bool ConfirmClearSettings() => false;
        public void ShowError(string message) { }
    }
}
