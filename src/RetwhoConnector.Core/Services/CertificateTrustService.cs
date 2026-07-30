using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Exceptions;
using RetwhoConnector.Core.Models;
using RetwhoConnector.Core.Security;

namespace RetwhoConnector.Core.Services;

public sealed class CertificateTrustService : ICertificateTrustService
{
    private readonly ICertificateProbe _probe;

    public CertificateTrustService()
        : this(new TlsCertificateProbe())
    {
    }

    internal CertificateTrustService(ICertificateProbe probe)
    {
        _probe = probe;
    }

    public async Task<PresentedCertificate> InspectAsync(
        Uri posBaseUri,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(posBaseUri);
        (X509Certificate2 certificate, SslPolicyErrors policyErrors) =
            await _probe.InspectAsync(posBaseUri, cancellationToken)
                .ConfigureAwait(false);
        using (certificate)
        {
            return new PresentedCertificate
            {
                Subject = certificate.Subject,
                Issuer = certificate.Issuer,
                ValidFromUtc = certificate.NotBefore.ToUniversalTime(),
                ValidToUtc = certificate.NotAfter.ToUniversalTime(),
                Sha256Fingerprint =
                    CertificateFingerprint.FromCertificate(certificate),
                PolicyErrors = policyErrors,
            };
        }
    }

    public bool ValidateForRequest(
        Uri configuredPosBaseUri,
        Uri requestUri,
        X509Certificate2 certificate,
        SslPolicyErrors policyErrors,
        string? approvedSha256)
    {
        ArgumentNullException.ThrowIfNull(configuredPosBaseUri);
        ArgumentNullException.ThrowIfNull(requestUri);
        ArgumentNullException.ThrowIfNull(certificate);

        if (policyErrors == SslPolicyErrors.None)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(approvedSha256) ||
            !SameOrigin(configuredPosBaseUri, requestUri))
        {
            return false;
        }

        return CertificateFingerprint.Equals(
            CertificateFingerprint.FromCertificate(certificate),
            approvedSha256);
    }

    private static bool SameOrigin(Uri expected, Uri actual) =>
        expected.Scheme.Equals(actual.Scheme, StringComparison.OrdinalIgnoreCase) &&
        expected.Host.Equals(actual.Host, StringComparison.OrdinalIgnoreCase) &&
        expected.Port == actual.Port;
}

internal sealed class TlsCertificateProbe : ICertificateProbe
{
    public async Task<(X509Certificate2 Certificate, SslPolicyErrors PolicyErrors)> InspectAsync(
        Uri origin,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(origin);
        X509Certificate2? captured = null;
        SslPolicyErrors capturedErrors = SslPolicyErrors.None;

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(
                origin.Host,
                origin.Port,
                cancellationToken).ConfigureAwait(false);
            await using var sslStream = new SslStream(
                client.GetStream(),
                leaveInnerStreamOpen: false,
                (_, certificate, _, policyErrors) =>
                {
                    if (certificate is not null)
                    {
                        captured = new X509Certificate2(certificate);
                    }

                    capturedErrors = policyErrors;
                    return true;
                });
            await sslStream.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions
                {
                    TargetHost = origin.Host,
                    EnabledSslProtocols = SslProtocols.None,
                    CertificateRevocationCheckMode =
                        X509RevocationMode.NoCheck,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is SocketException or AuthenticationException or IOException)
        {
            captured?.Dispose();
            throw new PosCertificateException(
                "POS_CERTIFICATE_UNTRUSTED",
                "The connector could not inspect the POS certificate.",
                exception);
        }

        return captured is null
            ? throw new PosCertificateException(
                "POS_CERTIFICATE_UNTRUSTED",
                "The POS did not present a certificate.")
            : (captured, capturedErrors);
    }
}
