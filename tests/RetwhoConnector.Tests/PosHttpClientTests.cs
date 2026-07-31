using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Configuration;
using RetwhoConnector.Core.Models;
using RetwhoConnector.Core.Services;

namespace RetwhoConnector.Tests;

public sealed class PosHttpClientTests
{
    [Fact]
    public async Task SendAsync_ReadsBoundedResponseAndLogsOnlySafeMetadata()
    {
        var log = new RecordingAgentLog();
        using var httpClient = new HttpClient(
            new StaticHandler(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<credential />"),
                }));
        IPosHttpClient transport = new PosHttpClient(
            httpClient,
            new PosResponseReader(),
            new PosOptions(),
            log);
        using HttpRequestMessage request =
            new PosHttpRequestFactory(new PosOptions())
                .CreateLogin(CreateSettings());

        PosHttpResponse result = await transport.SendAsync(
            request,
            CancellationToken.None);

        Assert.Equal(200, result.Metadata.StatusCode);
        RecordedLog entry = Assert.Single(log.Entries);
        Assert.Contains("validate", entry.Message, StringComparison.Ordinal);
        Assert.Contains("HTTP 200", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("FAKE_", entry.Message, StringComparison.Ordinal);
        Assert.Null(entry.Details);
    }

    [Fact]
    public async Task SendAsync_FailureDoesNotLogCredentialBearingUri()
    {
        var log = new RecordingAgentLog();
        using var httpClient = new HttpClient(new ThrowingHandler());
        IPosHttpClient transport = new PosHttpClient(
            httpClient,
            new PosResponseReader(),
            new PosOptions(),
            log);
        using HttpRequestMessage request =
            new PosHttpRequestFactory(new PosOptions())
                .CreateLogin(CreateSettings());

        await Assert.ThrowsAsync<HttpRequestException>(
            () => transport.SendAsync(request, CancellationToken.None));

        RecordedLog entry = Assert.Single(log.Entries);
        Assert.Equal(AgentLogLevel.Error, entry.Level);
        Assert.DoesNotContain("https://", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("FAKE_", entry.Message, StringComparison.Ordinal);
        Assert.Equal(
            typeof(HttpRequestException).FullName,
            entry.Details);
    }

    [Fact]
    public void Handler_RestrictsPosTlsToVersions12And13()
    {
        using var handler = Assert.IsType<HttpClientHandler>(
            PosHttpClientHandlerFactory.Create(
                new RejectingCertificateTrustService()));

        Assert.Equal(
            SslProtocols.Tls12 | SslProtocols.Tls13,
            handler.SslProtocols);
    }

    private static ConnectorSettings CreateSettings() =>
        new()
        {
            PosBaseUrl = "https://pos.example.test",
            PosUsername = "FAKE_USER",
            PosPassword = "FAKE_PASSWORD",
            LicenseKey = "FAKE-LICENSE-001",
        };

    private sealed class StaticHandler(HttpResponseMessage response)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException(
                $"Request failed for {request.RequestUri}");
    }

    private sealed class RecordingAgentLog : IAgentLog
    {
        public List<RecordedLog> Entries { get; } = [];
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
            string? correlationId = null)
        {
            Entries.Add(new RecordedLog(level, category, message, details));
            return true;
        }
    }

    private sealed record RecordedLog(
        AgentLogLevel Level,
        AgentLogCategory Category,
        string Message,
        string? Details);

    private sealed class RejectingCertificateTrustService
        : ICertificateTrustService
    {
        public Task<PresentedCertificate> InspectAsync(
            Uri posBaseUri,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public bool ValidateForRequest(
            Uri configuredPosBaseUri,
            Uri requestUri,
            X509Certificate2 certificate,
            System.Net.Security.SslPolicyErrors policyErrors,
            string? approvedSha256) => false;
    }
}
