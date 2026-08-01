using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using RetwhoConnector.Core.Configuration;
using RetwhoConnector.Core.Models;

namespace RetwhoConnector.Core.Services;

public sealed class PosHttpRequestFactory(PosOptions options)
{
    private static readonly XNamespace DomainNamespace =
        "urn:vfi-sapphire:np.domain.2001-07-01";
    private const string ReferentialDataset =
        "prodCodes,departments,ageValidations,taxRates,blueLaws,fees";

    internal static readonly HttpRequestOptionsKey<Uri> ConfiguredOriginKey =
        new("RetwhoConnector.ConfiguredPosOrigin");
    internal static readonly HttpRequestOptionsKey<string> CertificatePinKey =
        new("RetwhoConnector.CertificatePin");
    internal static readonly HttpRequestOptionsKey<string> CommandKey =
        new("RetwhoConnector.PosCommand");
    internal static readonly
        HttpRequestOptionsKey<CertificateValidationDecision>
        CertificateDecisionKey =
            new("RetwhoConnector.CertificateDecision");

    public HttpRequestMessage CreateLogin(ConnectorSettings settings)
    {
        string formLine = EncodeForm(
            [
                new("cmd", "validate"),
                new("user", settings.PosUsername),
                new("passwd", settings.PosPassword),
            ]);
        return Create(settings, "validate", formLine, formLine);
    }

    public HttpRequestMessage CreateVdatetime(
        ConnectorSettings settings,
        string cookie)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cookie);
        string formLine = EncodeForm(
            [
                new("cmd", "vdatetime"),
                new("cookie", cookie),
            ]);
        return Create(settings, "vdatetime", formLine, formLine);
    }

    public HttpRequestMessage CreatePluPage(
        ConnectorSettings settings,
        string cookie,
        PluPageQuery query)
    {
        string formLine = CreatePluFormLine(cookie);
        XElement selector = CreatePageSelector(query.Page, query.PageSize);
        return Create(
            settings,
            "vPLUs",
            formLine,
            selector.ToString(SaveOptions.DisableFormatting));
    }

    public HttpRequestMessage CreatePlu(
        ConnectorSettings settings,
        string cookie,
        PluLookupQuery query)
    {
        string formLine = CreatePluFormLine(cookie);
        var selector = new XElement(
            DomainNamespace + "PLUSelect",
            new XAttribute(XNamespace.Xmlns + "domain", DomainNamespace),
            new XElement(
                "query",
                new XElement(
                    "where",
                    new XElement(
                        "upc",
                        new XAttribute("source", "keyboard"),
                        query.Upc),
                    new XElement("upcModifier", query.UpcModifier))),
            new XElement("pageSize", 100),
            new XElement("page", 1));
        return Create(
            settings,
            "vPLUs",
            formLine,
            selector.ToString(SaveOptions.DisableFormatting));
    }

    public HttpRequestMessage CreateReferentialIntegrity(
        ConnectorSettings settings,
        string cookie)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cookie);
        string formLine =
            $"cmd=vrefinteg&dataset={ReferentialDataset}&cookie={Uri.EscapeDataString(cookie)}";
        return Create(settings, "vrefinteg", formLine, formLine);
    }

    private HttpRequestMessage Create(
        ConnectorSettings settings,
        string command,
        string query,
        string body)
    {
        Uri origin = new(settings.PosBaseUrl, UriKind.Absolute);
        var endpoint = new UriBuilder(origin)
        {
            Path = options.NaxmlPath,
            Query = query,
        };
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Uri)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };

        byte[] payloadBytes = Encoding.UTF8.GetBytes(body);
        request.Content = new ByteArrayContent(payloadBytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain")
        {
            CharSet = "UTF-8",
        };
        request.Content.Headers.ContentLength = payloadBytes.LongLength;

        request.Headers.UserAgent.ParseAdd(PosCompatibilityHeaders.UserAgent);
        request.Headers.Referrer = new Uri(origin, options.ConfigClientPath);
        request.Headers.Host = origin.IsDefaultPort ? origin.Host : origin.Authority;
        request.Headers.Connection.Add("keep-alive");
        Add(request, "Accept-Encoding", PosCompatibilityHeaders.AcceptEncoding);
        Add(request, "Accept-Language", PosCompatibilityHeaders.AcceptLanguage);
        Add(request, "Origin", origin.GetLeftPart(UriPartial.Authority));
        Add(request, "Sec-Fetch-Dest", PosCompatibilityHeaders.SecFetchDest);
        Add(request, "Sec-Fetch-Mode", PosCompatibilityHeaders.SecFetchMode);
        Add(request, "Sec-Fetch-Site", PosCompatibilityHeaders.SecFetchSite);
        Add(request, "sec-ch-ua", PosCompatibilityHeaders.SecChUa);
        Add(request, "sec-ch-ua-mobile", PosCompatibilityHeaders.SecChUaMobile);
        Add(request, "sec-ch-ua-platform", PosCompatibilityHeaders.SecChUaPlatform);
        request.Options.Set(ConfiguredOriginKey, origin);
        request.Options.Set(
            CertificateDecisionKey,
            new CertificateValidationDecision());
        request.Options.Set(
            CommandKey,
            command);
        if (!string.IsNullOrWhiteSpace(settings.PinnedCertificateSha256))
        {
            request.Options.Set(
                CertificatePinKey,
                settings.PinnedCertificateSha256);
        }

        return request;
    }

    private static string EncodeForm(
        IReadOnlyList<KeyValuePair<string, string>> parameters) =>
        string.Join(
            "&",
            parameters.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

    private static string CreatePluFormLine(string cookie)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cookie);
        return $"cmd=vPLUs&cookie={Uri.EscapeDataString(cookie)}";
    }

    private static XElement CreatePageSelector(int page, int pageSize) =>
        new(
            DomainNamespace + "PLUSelect",
            new XAttribute(XNamespace.Xmlns + "domain", DomainNamespace),
            new XElement("pageSize", pageSize),
            new XElement("page", page));

    private static void Add(
        HttpRequestMessage request,
        string name,
        string value)
    {
        if (!request.Headers.TryAddWithoutValidation(name, value))
        {
            throw new InvalidOperationException(
                $"The fixed POS compatibility header '{name}' could not be added.");
        }
    }
}

internal sealed class CertificateValidationDecision
{
    private int _rejected;

    public bool IsRejected =>
        Volatile.Read(ref _rejected) != 0;

    public void Reject() =>
        Interlocked.Exchange(ref _rejected, 1);
}
