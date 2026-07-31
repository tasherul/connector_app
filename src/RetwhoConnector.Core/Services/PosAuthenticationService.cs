using System.Net;
using System.Xml.Linq;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Configuration;
using RetwhoConnector.Core.Exceptions;
using RetwhoConnector.Core.Models;
using RetwhoConnector.Core.Security;

namespace RetwhoConnector.Core.Services;

public sealed class PosAuthenticationService : IPosAuthenticationService
{
    private readonly IPosHttpClient _httpClient;
    private readonly PosHttpRequestFactory _requestFactory;
    private readonly TimeProvider _timeProvider;

    public PosAuthenticationService(
        IPosHttpClient httpClient,
        PosHttpRequestFactory requestFactory,
        TimeProvider timeProvider)
    {
        _httpClient = httpClient;
        _requestFactory = requestFactory;
        _timeProvider = timeProvider;
    }

    internal PosAuthenticationService(
        HttpClient httpClient,
        PosHttpRequestFactory requestFactory,
        IPosResponseReader responseReader,
        PosOptions options,
        TimeProvider timeProvider)
        : this(
            new PosHttpClient(
                httpClient,
                responseReader,
                options,
                NullAgentLog.Instance,
                new LogSanitizer()),
            requestFactory,
            timeProvider)
    {
    }

    public async Task<PosSession> LoginAsync(
        ConnectorSettings settings,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = _requestFactory.CreateLogin(settings);
        PosHttpResponse posResponse = await _httpClient.SendAsync(
            request,
            cancellationToken).ConfigureAwait(false);
        var statusCode = (HttpStatusCode)posResponse.Metadata.StatusCode;

        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new PosAuthenticationException(
                "POS_LOGIN_FAILED",
                "The POS rejected the configured credentials.");
        }

        if (posResponse.Metadata.StatusCode is < 200 or > 299)
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
            ObtainedAtUtc = _timeProvider.GetUtcNow(),
        };
    }

    private static PosAuthenticationException LoginFailure(
        string message,
        Exception? innerException = null) =>
        new("POS_LOGIN_FAILED", message, innerException);
}
