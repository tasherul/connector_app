using System.Net;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Configuration;
using RetwhoConnector.Core.Exceptions;
using RetwhoConnector.Core.Models;
using RetwhoConnector.Core.Security;

namespace RetwhoConnector.Core.Services;

public sealed class PosDataService : IPosDataService
{
    private static readonly string[] AuthenticationSubjects =
        ["cookie", "session", "auth", "credential"];
    private static readonly string[] FailureIndicators =
        ["expired", "invalid", "unauthorized", "denied"];
    private readonly IPosHttpClient _httpClient;
    private readonly PosHttpRequestFactory _requestFactory;
    private readonly IVdatetimeXmlMapper _vdatetimeMapper;
    private readonly PluXmlMapper _pluMapper;
    private readonly ReferentialIntegrityXmlMapper _referentialIntegrityMapper;
    private readonly TimeProvider _timeProvider;

    public PosDataService(
        IPosHttpClient httpClient,
        PosHttpRequestFactory requestFactory,
        IVdatetimeXmlMapper vdatetimeMapper,
        PluXmlMapper pluMapper,
        ReferentialIntegrityXmlMapper referentialIntegrityMapper,
        TimeProvider timeProvider)
    {
        _httpClient = httpClient;
        _requestFactory = requestFactory;
        _vdatetimeMapper = vdatetimeMapper;
        _pluMapper = pluMapper;
        _referentialIntegrityMapper = referentialIntegrityMapper;
        _timeProvider = timeProvider;
    }

    internal PosDataService(
        HttpClient httpClient,
        PosHttpRequestFactory requestFactory,
        IPosResponseReader responseReader,
        IVdatetimeXmlMapper mapper,
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
            mapper,
            new PluXmlMapper(),
            new ReferentialIntegrityXmlMapper(),
            timeProvider)
    {
    }

    public async Task<VdatetimeResult> GetVdatetimeAsync(
        ConnectorSettings settings,
        string cookie,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cookie);
        using HttpRequestMessage request =
            _requestFactory.CreateVdatetime(settings, cookie);
        return await SendAndMapAsync(
            request,
            _vdatetimeMapper.Parse,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<PluPageResult> GetPluPageAsync(
        ConnectorSettings settings,
        string cookie,
        PluPageQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cookie);
        using HttpRequestMessage request =
            _requestFactory.CreatePluPage(settings, cookie, query);
        return await SendAndMapAsync(
            request,
            (xml, fetchedAtUtc) =>
                _pluMapper.ParsePage(xml, query, fetchedAtUtc),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<PluLookupResult> GetPluAsync(
        ConnectorSettings settings,
        string cookie,
        PluLookupQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cookie);
        using HttpRequestMessage request =
            _requestFactory.CreatePlu(settings, cookie, query);
        return await SendAndMapAsync(
            request,
            (xml, fetchedAtUtc) =>
                _pluMapper.ParseLookup(xml, query, fetchedAtUtc),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ReferentialIntegrityResult> GetReferentialIntegrityAsync(
        ConnectorSettings settings,
        string cookie,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cookie);
        using HttpRequestMessage request =
            _requestFactory.CreateReferentialIntegrity(settings, cookie);
        return await SendAndMapAsync(
            request,
            _referentialIntegrityMapper.Parse,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResult> SendAndMapAsync<TResult>(
        HttpRequestMessage request,
        Func<string, DateTimeOffset, TResult> map,
        CancellationToken cancellationToken)
    {
        PosHttpResponse posResponse = await _httpClient.SendAsync(
            request,
            cancellationToken).ConfigureAwait(false);
        var statusCode = (HttpStatusCode)posResponse.Metadata.StatusCode;

        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw SessionExpired();
        }

        if (posResponse.Metadata.StatusCode is < 200 or > 299)
        {
            throw new PosResponseException(
                "POS_HTTP_ERROR",
                "The POS data request failed.");
        }

        try
        {
            return map(
                posResponse.Body,
                _timeProvider.GetUtcNow());
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
        System.Xml.Linq.XDocument document;
        try
        {
            document = SecureXml.Parse(xml);
        }
        catch (PosResponseException)
        {
            return false;
        }

        PosXmlFaultDetails details =
            PosXmlFaultInspector.Inspect(document);
        if (details.IsLoginRequired)
        {
            return true;
        }

        string text = string.Join(
            " ",
            document
                .DescendantNodes()
                .OfType<System.Xml.Linq.XText>()
                .Select(node => node.Value));
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
