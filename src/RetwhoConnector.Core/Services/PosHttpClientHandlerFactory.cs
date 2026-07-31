using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using RetwhoConnector.Core.Abstractions;

namespace RetwhoConnector.Core.Services;

public static class PosHttpClientHandlerFactory
{
    public static HttpMessageHandler Create(
        ICertificateTrustService certificateTrustService) =>
        new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.None,
            SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            ServerCertificateCustomValidationCallback =
                (request, certificate, _, policyErrors) =>
                {
                    if (request.RequestUri is null || certificate is null ||
                        !request.Options.TryGetValue(
                            PosHttpRequestFactory.ConfiguredOriginKey,
                            out Uri? configuredOrigin))
                    {
                        return false;
                    }

                    request.Options.TryGetValue(
                        PosHttpRequestFactory.CertificatePinKey,
                        out string? approvedPin);
                    return certificateTrustService.ValidateForRequest(
                        configuredOrigin,
                        request.RequestUri,
                        certificate,
                        policyErrors,
                        approvedPin);
                },
        };
}
