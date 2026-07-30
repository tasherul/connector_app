using System.IO.Compression;
using System.Net;
using System.Text;
using RetwhoConnector.Core.Configuration;
using RetwhoConnector.Core.Exceptions;
using RetwhoConnector.Core.Models;
using RetwhoConnector.Core.Services;
using ZstdSharp;

namespace RetwhoConnector.Tests;

public sealed class PosProtocolEdgeTests
{
    [Theory]
    [InlineData("gzip")]
    [InlineData("deflate")]
    [InlineData("br")]
    [InlineData("zstd")]
    public async Task ResponseReader_DecodesAdvertisedEncoding(string encoding)
    {
        const string xml = "<sysDateTime><site>6720</site></sysDateTime>";
        byte[] encoded = await CompressAsync(Encoding.UTF8.GetBytes(xml), encoding);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(encoded),
        };
        response.Content.Headers.ContentEncoding.Add(encoding);

        PosHttpResponse result = await new PosResponseReader().ReadAsync(
            response,
            4096,
            CancellationToken.None);

        Assert.Equal(xml, result.Body);
    }

    [Fact]
    public async Task ResponseReader_DecodesStackedEncodingsInReverseOrder()
    {
        const string xml = "<sysDateTime><site>6720</site></sysDateTime>";
        byte[] gzip = await CompressAsync(Encoding.UTF8.GetBytes(xml), "gzip");
        byte[] brotli = await CompressAsync(gzip, "br");
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(brotli),
        };
        response.Content.Headers.ContentEncoding.Add("gzip");
        response.Content.Headers.ContentEncoding.Add("br");

        PosHttpResponse result = await new PosResponseReader().ReadAsync(
            response,
            4096,
            CancellationToken.None);

        Assert.Equal(xml, result.Body);
    }

    [Fact]
    public void XmlMapper_RejectsDtd()
    {
        const string xml =
            "<!DOCTYPE sysDateTime [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]>" +
            "<sysDateTime><site>&xxe;</site><sysDT>x</sysDT><sysTzId>x</sysTzId></sysDateTime>";

        PosResponseException exception = Assert.Throws<PosResponseException>(
            () => new VdatetimeXmlMapper().Parse(xml, DateTimeOffset.UtcNow));

        Assert.Equal("POS_INVALID_XML", exception.Code);
    }

    [Fact]
    public async Task Authentication_MissingCookieThrowsSafeError()
    {
        string xml = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "login-missing-cookie.xml"));
        using var client = new HttpClient(
            new StaticHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(xml, Encoding.UTF8, "text/xml"),
            }));
        var options = new PosOptions();
        var service = new PosAuthenticationService(
            client,
            new PosHttpRequestFactory(options),
            new PosResponseReader(),
            options,
            TimeProvider.System);

        PosAuthenticationException exception =
            await Assert.ThrowsAsync<PosAuthenticationException>(
                () => service.LoginAsync(CreateSettings(), CancellationToken.None));

        Assert.Equal("POS_LOGIN_FAILED", exception.Code);
        Assert.DoesNotContain("FAKE_PASSWORD", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResponseReader_RejectsDecompressedBodyAboveLimit()
    {
        byte[] encoded = await CompressAsync(
            Encoding.UTF8.GetBytes(new string('A', 10_000)),
            "gzip");
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(encoded),
        };
        response.Content.Headers.ContentEncoding.Add("gzip");

        PosResponseException exception = await Assert.ThrowsAsync<PosResponseException>(
            () => new PosResponseReader().ReadAsync(
                response,
                100,
                CancellationToken.None));

        Assert.Equal("POS_INVALID_RESPONSE", exception.Code);
    }

    private static async Task<byte[]> CompressAsync(
        byte[] bytes,
        string encoding)
    {
        using var output = new MemoryStream();
        await using (Stream stream = encoding switch
        {
            "gzip" => new GZipStream(output, CompressionMode.Compress, leaveOpen: true),
            "deflate" => new DeflateStream(output, CompressionMode.Compress, leaveOpen: true),
            "br" => new BrotliStream(output, CompressionMode.Compress, leaveOpen: true),
            "zstd" => new CompressionStream(output, leaveOpen: true),
            _ => throw new ArgumentOutOfRangeException(nameof(encoding)),
        })
        {
            await stream.WriteAsync(bytes);
        }

        return output.ToArray();
    }

    private static ConnectorSettings CreateSettings() =>
        new()
        {
            PosBaseUrl = "https://pos.example.test",
            PosUsername = "FAKE_USER",
            PosPassword = "FAKE_PASSWORD",
            LicenseKey = "FAKE-LICENSE-001",
        };

    private sealed class StaticHandler(HttpResponseMessage response)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }
}
