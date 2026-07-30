using System.Net;
using System.Xml.Linq;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Configuration;
using RetwhoConnector.Core.Exceptions;
using RetwhoConnector.Core.Models;

namespace RetwhoConnector.Core.Services;

public sealed class PosAuthenticationService(
    HttpClient httpClient,
    PosHttpRequestFactory requestFactory,
    IPosResponseReader responseReader,
    PosOptions options,
    TimeProvider timeProvider) : IPosAuthenticationService
{
    public async Task<PosSession> LoginAsync(
        ConnectorSettings settings,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = requestFactory.CreateLogin(settings);
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
            throw new PosAuthenticationException(
                "POS_LOGIN_FAILED",
                "The POS rejected the configured credentials.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new PosAuthenticationException(
                "POS_LOGIN_FAILED",
                "The POS login request failed.");
        }

        XDocument document = SecureXml.Parse(posResponse.Body);
        XElement root = document.Root
            ?? throw LoginFailure("The POS login response has no root element.");
        if (!root.Name.LocalName.Equals(
            "credential",
            StringComparison.OrdinalIgnoreCase))
        {
            throw LoginFailure("The POS returned an unexpected login response.");
        }

        string cookie;
        try
        {
            cookie = SecureXml.RequiredValue(root, "cookie");
        }
        catch (PosResponseException exception)
        {
            throw LoginFailure(
                "The POS login response did not include a session cookie.",
                exception);
        }

        string? site = root
            .Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName.Equals(
                    "site",
                    StringComparison.OrdinalIgnoreCase))
            ?.Value
            .Trim();
        return new PosSession
        {
            Cookie = cookie,
            SiteId = string.IsNullOrEmpty(site) ? null : site,
            ObtainedAtUtc = timeProvider.GetUtcNow(),
        };
    }

    private static PosAuthenticationException LoginFailure(
        string message,
        Exception? innerException = null) =>
        new("POS_LOGIN_FAILED", message, innerException);
}
