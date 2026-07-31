using Microsoft.Extensions.Logging.Abstractions;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Configuration;
using RetwhoConnector.Core.Exceptions;
using RetwhoConnector.Core.Models;
using RetwhoConnector.Core.Services;

namespace RetwhoConnector.Tests;

public sealed class AgentOrchestrationServiceTests
{
    [Fact]
    public async Task SaveTestAndConnect_PosFailureLeavesOldSettingsUntouched()
    {
        ConnectorSettings oldSettings = CreateSettings();
        var settings = new FakeSettingsService(oldSettings);
        var authentication = new FakeAuthenticationService
        {
            Exception = new PosAuthenticationException(
                "POS_LOGIN_FAILED",
                "The POS rejected the configured credentials."),
        };
        var bridge = new FakeBridgeClient();
        AgentOrchestrationService service = CreateService(
            settings,
            authentication,
            bridge);
        ConnectorSettings draft = CreateSettings() with
        {
            PosUsername = "FAKE_NEW_USER",
            PosPassword = "FAKE_NEW_PASSWORD",
        };

        await Assert.ThrowsAsync<PosAuthenticationException>(
            () => service.SaveTestAndConnectAsync(
                draft,
                CancellationToken.None));

        Assert.Equal(oldSettings, settings.Settings);
        Assert.Equal(0, settings.SaveCalls);
        Assert.Equal(0, bridge.ConnectCalls);
    }

    [Fact]
    public async Task SaveTestAndConnect_CloudFailureRetainsFreshPosSession()
    {
        var settings = new FakeSettingsService(CreateSettings());
        var authentication = new FakeAuthenticationService();
        var bridge = new FakeBridgeClient
        {
            ConnectException = new InvalidOperationException(
                "Cloud unavailable."),
        };
        AgentOrchestrationService service = CreateService(
            settings,
            authentication,
            bridge);
        ConnectorSettings draft = CreateSettings() with
        {
            PosUsername = "FAKE_NEW_USER",
            PosPassword = "FAKE_NEW_PASSWORD",
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SaveTestAndConnectAsync(
                draft,
                CancellationToken.None));

        Assert.Equal("FAKE_NEW_USER", settings.Settings?.PosUsername);
        Assert.Equal("FAKE_NEW_PASSWORD", settings.Settings?.PosPassword);
        Assert.Equal("FAKE_FRESH_COOKIE", settings.Settings?.PosCookie);
        Assert.Equal(1, authentication.Calls);
        Assert.Equal(1, bridge.ConnectCalls);
    }

    [Fact]
    public async Task SaveTestAndConnect_SuccessConnectsWithSavedLicense()
    {
        var settings = new FakeSettingsService(null);
        var authentication = new FakeAuthenticationService();
        var bridge = new FakeBridgeClient();
        AgentOrchestrationService service = CreateService(
            settings,
            authentication,
            bridge);

        await service.SaveTestAndConnectAsync(
            CreateSettings(),
            CancellationToken.None);

        Assert.Equal("FAKE_FRESH_COOKIE", settings.Settings?.PosCookie);
        Assert.Equal("FAKE-LICENSE-001", bridge.LastLicenseKey);
        Assert.True(bridge.IsRegistered);
    }

    [Fact]
    public async Task InspectCertificate_NormalizesHttpsOrigin()
    {
        var certificateTrust = new RecordingCertificateTrustService();
        AgentOrchestrationService service = CreateService(
            new FakeSettingsService(null),
            new FakeAuthenticationService(),
            new FakeBridgeClient(),
            certificateTrust);

        await service.InspectCertificateAsync(
            "https://pos.example.test:443/",
            CancellationToken.None);

        Assert.Equal(
            new Uri("https://pos.example.test"),
            certificateTrust.LastOrigin);
    }

    private static AgentOrchestrationService CreateService(
        FakeSettingsService settings,
        FakeAuthenticationService authentication,
        FakeBridgeClient bridge,
        ICertificateTrustService? certificateTrust = null)
    {
        var coordinator = new ConnectorCoordinator(
            settings,
            authentication,
            new FakeDataService(),
            bridge,
            new ActionExecutionRegistry(
                TimeProvider.System,
                CancellationToken.None),
            new BridgeOptions(),
            TimeProvider.System,
            NullLogger<ConnectorCoordinator>.Instance,
            CancellationToken.None);
        return new AgentOrchestrationService(
            coordinator,
            settings,
            authentication,
            certificateTrust ?? new RecordingCertificateTrustService(),
            new RecordingAgentLog());
    }

    private static ConnectorSettings CreateSettings() =>
        new()
        {
            PosBaseUrl = "https://pos.example.test",
            PosUsername = "FAKE_USER",
            PosPassword = "FAKE_PASSWORD",
            LicenseKey = "FAKE-LICENSE-001",
            PosCookie = "FAKE_OLD_COOKIE",
            AutoConnect = false,
        };

    private sealed class FakeSettingsService(ConnectorSettings? settings)
        : ISecureSettingsService
    {
        public ConnectorSettings? Settings { get; private set; } = settings;
        public int SaveCalls { get; private set; }

        public Task<ConnectorSettings?> LoadAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(Settings);

        public Task SaveAsync(
            ConnectorSettings value,
            CancellationToken cancellationToken)
        {
            SaveCalls++;
            Settings = value;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            Settings = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuthenticationService
        : IPosAuthenticationService
    {
        public Exception? Exception { get; init; }
        public int Calls { get; private set; }

        public Task<PosSession> LoginAsync(
            ConnectorSettings settings,
            CancellationToken cancellationToken)
        {
            Calls++;
            if (Exception is not null)
            {
                return Task.FromException<PosSession>(Exception);
            }

            return Task.FromResult(new PosSession
            {
                Cookie = "FAKE_FRESH_COOKIE",
                SiteId = "6720",
                ObtainedAtUtc = DateTimeOffset.UtcNow,
            });
        }
    }

    private sealed class FakeDataService : IPosDataService
    {
        public Task<VdatetimeResult> GetVdatetimeAsync(
            ConnectorSettings settings,
            string cookie,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "POS data is not used by these tests.");
    }

    private sealed class FakeBridgeClient : IBridgeSocketClient
    {
        public Exception? ConnectException { get; init; }
        public int ConnectCalls { get; private set; }
        public string? LastLicenseKey { get; private set; }
        public bool IsTransportConnected { get; private set; }
        public bool IsRegistered { get; private set; }

        public event EventHandler<BridgeConnectionStateChangedEventArgs>?
            StateChanged
        {
            add { }
            remove { }
        }

        public event Func<BridgeActionContext, CancellationToken, Task>?
            ActionReceived
        {
            add { }
            remove { }
        }

        public event EventHandler? SessionReplaced
        {
            add { }
            remove { }
        }

        public Task ConnectAsync(
            string licenseKey,
            CancellationToken cancellationToken)
        {
            ConnectCalls++;
            LastLicenseKey = licenseKey;
            if (ConnectException is not null)
            {
                return Task.FromException(ConnectException);
            }

            IsTransportConnected = true;
            IsRegistered = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            IsTransportConnected = false;
            IsRegistered = false;
            return Task.CompletedTask;
        }

        public Task<AgentDataPushResponse> PushAgentDataAsync(
            object payload,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingCertificateTrustService
        : ICertificateTrustService
    {
        public Uri? LastOrigin { get; private set; }

        public Task<PresentedCertificate> InspectAsync(
            Uri posBaseUri,
            CancellationToken cancellationToken)
        {
            LastOrigin = posBaseUri;
            return Task.FromResult(new PresentedCertificate
            {
                Subject = "CN=pos.example.test",
                Issuer = "CN=FAKE_CA",
                ValidFromUtc = DateTimeOffset.UtcNow.AddDays(-1),
                ValidToUtc = DateTimeOffset.UtcNow.AddDays(1),
                Sha256Fingerprint = new string('A', 64),
                PolicyErrors =
                    System.Net.Security.SslPolicyErrors
                        .RemoteCertificateChainErrors,
            });
        }

        public bool ValidateForRequest(
            Uri configuredPosBaseUri,
            Uri requestUri,
            System.Security.Cryptography.X509Certificates.X509Certificate2
                certificate,
            System.Net.Security.SslPolicyErrors policyErrors,
            string? approvedSha256) => false;
    }

    private sealed class RecordingAgentLog : IAgentLog
    {
        public LogPipelineHealth CurrentHealth { get; } =
            new(LoggingHealthState.Healthy, 0, "Healthy.");

        public event EventHandler<LogPipelineHealth>? HealthChanged
        {
            add { }
            remove { }
        }

        public bool TryWrite(
            AgentLogLevel level,
            AgentLogCategory category,
            string message,
            string? details = null,
            string? correlationId = null) => true;
    }
}
