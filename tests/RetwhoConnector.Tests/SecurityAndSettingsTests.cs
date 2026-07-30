using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Models;
using RetwhoConnector.Core.Security;
using RetwhoConnector.Core.Services;

namespace RetwhoConnector.Tests;

public sealed class SecurityAndSettingsTests
{
    [Theory]
    [InlineData(
        "cmd=validate&user=FAKE_USER&passwd=super-secret",
        "cmd=validate&user=<redacted>&passwd=<redacted>")]
    [InlineData(
        "cookie=FAKE_COOKIE&password=secret",
        "cookie=<redacted>&password=<redacted>")]
    [InlineData(
        "Authorization: Bearer token-value",
        "Authorization: <redacted>")]
    public void Redactor_RemovesSensitiveValues(string input, string expected)
    {
        Assert.Equal(expected, SensitiveDataRedactor.Redact(input));
    }

    [Fact]
    public void Redactor_DoesNotDamageNormalStatus()
    {
        const string message = "POS validate request failed with status 503.";

        Assert.Equal(message, SensitiveDataRedactor.Redact(message));
    }

    [Fact]
    public async Task Settings_SaveAndLoad_EncryptsEverySecret()
    {
        var store = new MemorySettingsFileStore();
        var protector = new TestSecretProtector();
        var service = new SecureSettingsService(
            store,
            protector,
            "/safe/settings.json");
        ConnectorSettings settings = CreateSettings();

        await service.SaveAsync(settings, CancellationToken.None);
        ConnectorSettings? loaded = await service.LoadAsync(CancellationToken.None);

        Assert.Equal(settings, loaded);
        Assert.DoesNotContain(settings.PosUsername, store.Content, StringComparison.Ordinal);
        Assert.DoesNotContain(settings.PosPassword, store.Content, StringComparison.Ordinal);
        Assert.DoesNotContain(settings.LicenseKey, store.Content, StringComparison.Ordinal);
        Assert.DoesNotContain(settings.PosCookie!, store.Content, StringComparison.Ordinal);
        Assert.DoesNotContain(
            settings.PinnedCertificateSha256!,
            store.Content,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Settings_ChangingPosHost_ClearsCookieAndPin()
    {
        var store = new MemorySettingsFileStore();
        var service = new SecureSettingsService(
            store,
            new TestSecretProtector(),
            "/safe/settings.json");
        await service.SaveAsync(CreateSettings(), CancellationToken.None);

        await service.SaveAsync(
            CreateSettings() with { PosBaseUrl = "https://other-pos.example.test" },
            CancellationToken.None);
        ConnectorSettings loaded =
            Assert.IsType<ConnectorSettings>(
                await service.LoadAsync(CancellationToken.None));

        Assert.Null(loaded.PosCookie);
        Assert.Null(loaded.PinnedCertificateSha256);
    }

    [Fact]
    public async Task Settings_ChangingCredentials_ClearsCookie()
    {
        var store = new MemorySettingsFileStore();
        var service = new SecureSettingsService(
            store,
            new TestSecretProtector(),
            "/safe/settings.json");
        await service.SaveAsync(CreateSettings(), CancellationToken.None);

        await service.SaveAsync(
            CreateSettings() with { PosPassword = "FAKE_CHANGED_PASSWORD" },
            CancellationToken.None);
        ConnectorSettings loaded =
            Assert.IsType<ConnectorSettings>(
                await service.LoadAsync(CancellationToken.None));

        Assert.Null(loaded.PosCookie);
        Assert.NotNull(loaded.PinnedCertificateSha256);
    }

    [Fact]
    public void CertificatePin_RequiresExactOriginAndFingerprint()
    {
        using X509Certificate2 certificate = CreateCertificate();
        var service = new CertificateTrustService(new FakeCertificateProbe());
        string fingerprint = CertificateFingerprint.FromCertificate(certificate);

        Assert.True(service.ValidateForRequest(
            new Uri("https://pos.example.test"),
            new Uri("https://pos.example.test/cgi-bin/NAXML"),
            certificate,
            SslPolicyErrors.RemoteCertificateChainErrors,
            fingerprint));
        Assert.False(service.ValidateForRequest(
            new Uri("https://pos.example.test"),
            new Uri("https://other-pos.example.test/cgi-bin/NAXML"),
            certificate,
            SslPolicyErrors.RemoteCertificateChainErrors,
            fingerprint));
        Assert.False(service.ValidateForRequest(
            new Uri("https://pos.example.test"),
            new Uri("https://pos.example.test/cgi-bin/NAXML"),
            certificate,
            SslPolicyErrors.RemoteCertificateChainErrors,
            new string('0', 64)));
    }

    [Fact]
    public void CertificatePin_SystemTrustDoesNotRequirePin()
    {
        using X509Certificate2 certificate = CreateCertificate();
        var service = new CertificateTrustService(new FakeCertificateProbe());

        Assert.True(service.ValidateForRequest(
            new Uri("https://pos.example.test"),
            new Uri("https://pos.example.test/cgi-bin/NAXML"),
            certificate,
            SslPolicyErrors.None,
            null));
    }

    private static ConnectorSettings CreateSettings() =>
        new()
        {
            PosBaseUrl = "https://pos.example.test",
            PosUsername = "FAKE_USER",
            PosPassword = "FAKE_PASSWORD",
            LicenseKey = "FAKE-LICENSE-001",
            PosCookie = "FAKE_COOKIE",
            PinnedCertificateSha256 = new string('A', 64),
            AutoConnect = true,
        };

    private static X509Certificate2 CreateCertificate()
    {
        using RSA rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=pos.example.test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));
    }

    private sealed class MemorySettingsFileStore : ISettingsFileStore
    {
        public string Content { get; private set; } = string.Empty;

        public Task<string?> ReadAsync(
            string path,
            CancellationToken cancellationToken) =>
            Task.FromResult<string?>(
                string.IsNullOrEmpty(Content) ? null : Content);

        public Task WriteAtomicAsync(
            string path,
            string content,
            CancellationToken cancellationToken)
        {
            Content = content;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(
            string path,
            CancellationToken cancellationToken)
        {
            Content = string.Empty;
            return Task.CompletedTask;
        }
    }

    private sealed class TestSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) =>
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"protected:{plaintext}"));

        public string Unprotect(string ciphertext)
        {
            string value = System.Text.Encoding.UTF8.GetString(
                Convert.FromBase64String(ciphertext));
            return value["protected:".Length..];
        }
    }

    private sealed class FakeCertificateProbe : ICertificateProbe
    {
        public Task<(X509Certificate2 Certificate, SslPolicyErrors PolicyErrors)> InspectAsync(
            Uri origin,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Network probing is not used by these tests.");
    }
}
