using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Configuration;
using RetwhoConnector.Core.Exceptions;
using RetwhoConnector.Core.Models;
using RetwhoConnector.Core.Security;
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
            log,
            new LogSanitizer());
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
        Assert.NotNull(entry.Details);
        using JsonDocument diagnostic =
            JsonDocument.Parse(entry.Details);
        JsonElement requestDiagnostic =
            diagnostic.RootElement.GetProperty("request");
        Assert.Equal(
            "validate",
            requestDiagnostic.GetProperty("command").GetString());
        Assert.Equal(
            "POST",
            requestDiagnostic.GetProperty("method").GetString());
        Assert.Equal(
            "1.1",
            requestDiagnostic.GetProperty("version").GetString());
        Assert.True(
            requestDiagnostic.GetProperty("contentLength").GetInt64() > 0);
        Assert.False(
            requestDiagnostic.GetProperty("hasCertificatePin").GetBoolean());
        JsonElement responseDiagnostic =
            diagnostic.RootElement.GetProperty("response");
        Assert.Equal(
            200,
            responseDiagnostic.GetProperty("statusCode").GetInt32());
        Assert.Equal(
            "credential",
            responseDiagnostic.GetProperty("rootName").GetString());
        Assert.True(
            responseDiagnostic
                .GetProperty("responseCharacters")
                .GetInt32() > 0);
        AssertSafeDiagnostic(entry.Details);
    }

    [Fact]
    public async Task SendAsync_FaultLogsOnlyAllowlistedXmlFields()
    {
        string xml = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "pos-login-required.xml"));
        var log = new RecordingAgentLog();
        using var httpClient = new HttpClient(
            new StaticHandler(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(xml),
                }));
        IPosHttpClient transport = new PosHttpClient(
            httpClient,
            new PosResponseReader(),
            new PosOptions(),
            log,
            new LogSanitizer());
        using HttpRequestMessage request =
            new PosHttpRequestFactory(new PosOptions())
                .CreateVdatetime(
                    CreateSettings(),
                    "FAKE_COOKIE");

        await transport.SendAsync(
            request,
            CancellationToken.None);

        RecordedLog entry = Assert.Single(log.Entries);
        Assert.Equal(AgentLogLevel.Information, entry.Level);
        Assert.Equal(AgentLogCategory.Session, entry.Category);
        Assert.NotNull(entry.Details);
        Assert.Contains(
            "CGIPortal.LoginRequired",
            entry.Details,
            StringComparison.Ordinal);
        Assert.Contains(
            "CGIPortal Error",
            entry.Details,
            StringComparison.Ordinal);
        Assert.Contains(
            "No Credential for the User.",
            entry.Details,
            StringComparison.Ordinal);
        AssertSafeDiagnostic(entry.Details);
        Assert.DoesNotContain(
            "<VFI:Response",
            entry.Details,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("page", "vPLUs")]
    [InlineData("lookup", "vPLUs")]
    [InlineData("referential", "vrefinteg")]
    public async Task SendAsync_NewDataRequestsLogOnlySafeMetadata(
        string operation,
        string expectedCommand)
    {
        string responseBody = operation == "referential"
            ? File.ReadAllText(Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "referential-integrity-success.xml"))
            : File.ReadAllText(Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "plu-page-success.xml"));
        var log = new RecordingAgentLog();
        using var httpClient = new HttpClient(
            new StaticHandler(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseBody),
                }));
        IPosHttpClient transport = new PosHttpClient(
            httpClient,
            new PosResponseReader(),
            new PosOptions(),
            log,
            new LogSanitizer());
        var factory = new PosHttpRequestFactory(new PosOptions());
        using HttpRequestMessage request = operation switch
        {
            "page" => factory.CreatePluPage(
                CreateSettings(),
                "FAKE_COOKIE",
                new PluPageQuery(2, 25)),
            "lookup" => factory.CreatePlu(
                CreateSettings(),
                "FAKE_COOKIE",
                new PluLookupQuery("00000000000001", "000")),
            "referential" => factory.CreateReferentialIntegrity(
                CreateSettings(),
                "FAKE_COOKIE"),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        await transport.SendAsync(request, CancellationToken.None);

        RecordedLog entry = Assert.Single(log.Entries);
        Assert.Contains(expectedCommand, entry.Message, StringComparison.Ordinal);
        Assert.NotNull(entry.Details);
        using JsonDocument diagnostic = JsonDocument.Parse(entry.Details);
        Assert.Equal(
            expectedCommand,
            diagnostic.RootElement
                .GetProperty("request")
                .GetProperty("command")
                .GetString());
        Assert.True(
            diagnostic.RootElement
                .GetProperty("request")
                .GetProperty("contentLength")
                .GetInt64() > 0);
        Assert.Equal(
            200,
            diagnostic.RootElement
                .GetProperty("response")
                .GetProperty("statusCode")
                .GetInt32());
        Assert.Equal(
            responseBody.Length,
            diagnostic.RootElement
                .GetProperty("response")
                .GetProperty("responseCharacters")
                .GetInt32());
        Assert.True(
            diagnostic.RootElement
                .GetProperty("elapsedMilliseconds")
                .GetInt64() >= 0);
        string completeEntry = entry.Message + entry.Details;
        AssertSafeDiagnostic(completeEntry);
        Assert.DoesNotContain("PLUSelect", completeEntry, StringComparison.Ordinal);
        Assert.DoesNotContain("FAKE PRODUCT", completeEntry, StringComparison.Ordinal);
        Assert.DoesNotContain("FAKE-SITE-17", completeEntry, StringComparison.Ordinal);
        Assert.DoesNotContain("FAKE TAX", completeEntry, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "https://pos.example.test",
            completeEntry,
            StringComparison.OrdinalIgnoreCase);
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
            log,
            new LogSanitizer());
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
            JsonDocument
                .Parse(entry.Details!)
                .RootElement
                .GetProperty("exceptionType")
                .GetString());
        AssertSafeDiagnostic(entry.Details!);
    }

    [Fact]
    public async Task SendAsync_RejectedPinnedCertificateMapsToChanged()
    {
        var log = new RecordingAgentLog();
        using var httpClient = new HttpClient(
            new CertificateRejectingHandler());
        IPosHttpClient transport = new PosHttpClient(
            httpClient,
            new PosResponseReader(),
            new PosOptions(),
            log,
            new LogSanitizer());
        using HttpRequestMessage request =
            new PosHttpRequestFactory(new PosOptions())
                .CreateLogin(CreateSettings() with
                {
                    PinnedCertificateSha256 = new string('A', 64),
                });

        PosCertificateException exception =
            await Assert.ThrowsAsync<PosCertificateException>(
                () => transport.SendAsync(
                    request,
                    CancellationToken.None));

        Assert.Equal("POS_CERTIFICATE_CHANGED", exception.Code);
        RecordedLog entry = Assert.Single(log.Entries);
        Assert.Contains(
            "POS_CERTIFICATE_CHANGED",
            entry.Details,
            StringComparison.Ordinal);
        AssertSafeDiagnostic(entry.Details!);
    }

    [Fact]
    public async Task SendAsync_RejectedUnpinnedCertificateMapsToUntrusted()
    {
        var log = new RecordingAgentLog();
        using var httpClient = new HttpClient(
            new CertificateRejectingHandler());
        IPosHttpClient transport = new PosHttpClient(
            httpClient,
            new PosResponseReader(),
            new PosOptions(),
            log,
            new LogSanitizer());
        using HttpRequestMessage request =
            new PosHttpRequestFactory(new PosOptions())
                .CreateLogin(CreateSettings());

        PosCertificateException exception =
            await Assert.ThrowsAsync<PosCertificateException>(
                () => transport.SendAsync(
                    request,
                    CancellationToken.None));

        Assert.Equal("POS_CERTIFICATE_UNTRUSTED", exception.Code);
        RecordedLog entry = Assert.Single(log.Entries);
        Assert.Contains(
            "POS_CERTIFICATE_UNTRUSTED",
            entry.Details,
            StringComparison.Ordinal);
        AssertSafeDiagnostic(entry.Details!);
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

    private static void AssertSafeDiagnostic(string details)
    {
        Assert.DoesNotContain(
            "FAKE_PASSWORD",
            details,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "FAKE_COOKIE",
            details,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "FAKE_USER",
            details,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "https://",
            details,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            " at System.",
            details,
            StringComparison.Ordinal);
    }

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

    private sealed class CertificateRejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.True(request.Options.TryGetValue(
                PosHttpRequestFactory.CertificateDecisionKey,
                out CertificateValidationDecision? decision));
            Assert.NotNull(decision);
            decision.Reject();
            throw new HttpRequestException(
                "The fake certificate callback rejected the certificate.");
        }
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
