using System.Net;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Configuration;
using RetwhoConnector.Core.Exceptions;
using RetwhoConnector.Core.Models;

namespace RetwhoConnector.Core.Services;

public sealed class PosDataService(
    HttpClient httpClient,
    PosHttpRequestFactory requestFactory,
    IPosResponseReader responseReader,
    IVdatetimeXmlMapper mapper,
    PosOptions options,
    TimeProvider timeProvider) : IPosDataService
{
    private static readonly string[] AuthenticationSubjects =
        ["cookie", "session", "auth", "credential"];
    private static readonly string[] FailureIndicators =
        ["expired", "invalid", "unauthorized", "denied"];

    public async Task<VdatetimeResult> GetVdatetimeAsync(
        ConnectorSettings settings,
        string cookie,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cookie);
        using HttpRequestMessage request =
            requestFactory.CreateVdatetime(settings, cookie);
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        PosHttpResponse posResponse = await responseReader.ReadAsync(
            response,
            options.MaximumResponseBytes,
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw SessionExpired();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new PosResponseException(
                "POS_HTTP_ERROR",
                "The POS data request failed.");
        }

        try
        {
            return mapper.Parse(posResponse.Body, timeProvider.GetUtcNow());
        }
        catch (PosResponseException exception)
            when (exception.Code == "POS_INVALID_RESPONSE" &&
                  LooksLikeSessionExpiry(posResponse.Body))
        {
            throw SessionExpired(exception);
        }
    }

    private static bool LooksLikeSessionExpiry(string xml)
    {
        string text;
        try
        {
            text = string.Join(
                " ",
                SecureXml.Parse(xml).DescendantNodes().OfType<System.Xml.Linq.XText>()
                    .Select(node => node.Value));
        }
        catch (PosResponseException)
        {
            return false;
        }

        return AuthenticationSubjects.Any(subject =>
                   text.Contains(subject, StringComparison.OrdinalIgnoreCase)) &&
               FailureIndicators.Any(indicator =>
                   text.Contains(indicator, StringComparison.OrdinalIgnoreCase));
    }

    private static PosAuthenticationException SessionExpired(
        Exception? innerException = null) =>
        new(
            "POS_AUTH_EXPIRED",
            "The saved POS session is no longer valid.",
            innerException);
}
