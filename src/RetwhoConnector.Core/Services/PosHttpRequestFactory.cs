using System.Net;
using System.Net.Http.Headers;
using System.Text;
using RetwhoConnector.Core.Configuration;
using RetwhoConnector.Core.Models;

namespace RetwhoConnector.Core.Services;

public sealed class PosHttpRequestFactory(PosOptions options)
{
    internal static readonly HttpRequestOptionsKey<Uri> ConfiguredOriginKey =
        new("RetwhoConnector.ConfiguredPosOrigin");
    internal static readonly HttpRequestOptionsKey<string> CertificatePinKey =
        new("RetwhoConnector.CertificatePin");
    internal static readonly HttpRequestOptionsKey<string> CommandKey =
        new("RetwhoConnector.PosCommand");

    public HttpRequestMessage CreateLogin(ConnectorSettings settings) =>
        Create(
            settings,
            [
                new("cmd", "validate"),
                new("user", settings.PosUsername),
                new("passwd", settings.PosPassword),
            ]);

    public HttpRequestMessage CreateVdatetime(
        ConnectorSettings settings,
        string cookie)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cookie);
        return Create(
            settings,
            [
                new("cmd", "vdatetime"),
                new("cookie", cookie),
            ]);
    }

    private HttpRequestMessage Create(
        ConnectorSettings settings,
        IReadOnlyList<KeyValuePair<string, string>> parameters)
    {
        Uri origin = new(settings.PosBaseUrl, UriKind.Absolute);
        string payload = string.Join(
            "&",
            parameters.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        var endpoint = new UriBuilder(origin)
        {
            Path = options.NaxmlPath,
            Query = payload,
        };
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Uri)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };

        byte[] payloadBytes = Encoding.UTF8.GetBytes(payload);
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
            CommandKey,
            parameters.First(pair =>
                pair.Key.Equals("cmd", StringComparison.Ordinal)).Value);
        if (!string.IsNullOrWhiteSpace(settings.PinnedCertificateSha256))
        {
            request.Options.Set(
                CertificatePinKey,
                settings.PinnedCertificateSha256);
        }

        return request;
    }

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
