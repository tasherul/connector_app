using System.Net;
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
