using System.IO.Compression;
using System.Net;
using System.Text;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Configuration;
using RetwhoConnector.Core.Exceptions;
using RetwhoConnector.Core.Models;
using RetwhoConnector.Core.Services;

namespace RetwhoConnector.Tests;

public sealed class PosProtocolTests
{
    [Fact]
    public async Task PluPageRequest_UsesExactSelectorBytes()
    {
        var factory = new PosHttpRequestFactory(new PosOptions());
        using HttpRequestMessage request = factory.CreatePluPage(
            CreateSettings(),
            "FAKE_COOKIE",
            new PluPageQuery(2, 25));

        Assert.Equal(
            "cmd=vPLUs&cookie=FAKE_COOKIE",
            request.RequestUri!.Query.TrimStart('?'));
        const string expected =
            "<domain:PLUSelect xmlns:domain=\"urn:vfi-sapphire:np.domain.2001-07-01\">" +
            "<pageSize>25</pageSize><page>2</page></domain:PLUSelect>";
        Assert.Equal(expected, await request.Content!.ReadAsStringAsync());

        AssertRequestCompatibility(request, "vPLUs", expected);
    }

    [Fact]
    public async Task PluRequest_UsesExactKeyboardLookupSelector()
    {
        var factory = new PosHttpRequestFactory(new PosOptions());
        using HttpRequestMessage request = factory.CreatePlu(
            CreateSettings(),
            "FAKE_COOKIE",
            new PluLookupQuery("00000000000001", "000"));

        Assert.Equal(
            "cmd=vPLUs&cookie=FAKE_COOKIE",
            request.RequestUri!.Query.TrimStart('?'));
        const string expected =
            "<domain:PLUSelect xmlns:domain=\"urn:vfi-sapphire:np.domain.2001-07-01\">" +
            "<query><where><upc source=\"keyboard\">00000000000001</upc>" +
            "<upcModifier>000</upcModifier></where></query>" +
            "<pageSize>100</pageSize><page>1</page></domain:PLUSelect>";
        Assert.Equal(expected, await request.Content!.ReadAsStringAsync());

        AssertRequestCompatibility(request, "vPLUs", expected);
    }

    [Fact]
    public async Task ReferentialIntegrityRequest_UsesFixedLiteralDataset()
    {
        var factory = new PosHttpRequestFactory(new PosOptions());
        using HttpRequestMessage request = factory.CreateReferentialIntegrity(
            CreateSettings(),
            "FAKE_COOKIE");
        const string expected =
            "cmd=vrefinteg&dataset=prodCodes,departments,ageValidations,taxRates,blueLaws,fees&cookie=FAKE_COOKIE";

        Assert.Equal(expected, request.RequestUri!.Query.TrimStart('?'));
        Assert.Equal(expected, await request.Content!.ReadAsStringAsync());

        AssertRequestCompatibility(request, "vrefinteg", expected);
    }

    [Fact]
    public async Task LoginRequest_UsesExactCompatibilityProfile()
    {
        var factory = new PosHttpRequestFactory(new PosOptions());
        using HttpRequestMessage request = factory.CreateLogin(CreateSettings());
        string body = await request.Content!.ReadAsStringAsync();

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(HttpVersion.Version11, request.Version);
        Assert.Equal("pos.example.test", request.Headers.Host);
        Assert.Equal(
            PosCompatibilityHeaders.UserAgent,
            request.Headers.UserAgent.ToString());
        Assert.Equal(
            PosCompatibilityHeaders.AcceptEncoding,
            string.Join(", ", request.Headers.GetValues("Accept-Encoding")));
        Assert.Equal("text/plain; charset=UTF-8", request.Content.Headers.ContentType!.ToString());
        Assert.Equal(Encoding.UTF8.GetByteCount(body), request.Content.Headers.ContentLength);
        Assert.Equal(request.RequestUri!.Query.TrimStart('?'), body);
        Assert.DoesNotContain("Accept", request.Headers.Select(header => header.Key));
    }

    [Fact]
    public async Task ResponseReader_DecodesGzipAndCapturesSafeHeaders()
    {
        const string xml = "<credential><cookie>FAKE_COOKIE</cookie></credential>";
        byte[] compressed;
        using (var output = new MemoryStream())
        {
            using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
            {
                await gzip.WriteAsync(Encoding.UTF8.GetBytes(xml));
            }

            compressed = output.ToArray();
        }

        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(compressed),
        };
        response.Content.Headers.ContentEncoding.Add("gzip");
        response.Headers.TryAddWithoutValidation("Set-Cookie", "SECRET_HTTP_COOKIE");
        var reader = new PosResponseReader();

        PosHttpResponse result = await reader.ReadAsync(
            response,
            1024,
            CancellationToken.None);

        Assert.Equal(xml, result.Body);
        Assert.True(result.Metadata.HasSetCookieHeader);
        Assert.DoesNotContain("SECRET_HTTP_COOKIE", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResponseReader_RejectsUnknownEncoding()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<xml />"),
        };
        response.Content.Headers.ContentEncoding.Add("unknown");
        var reader = new PosResponseReader();

        PosResponseException exception = await Assert.ThrowsAsync<PosResponseException>(
            () => reader.ReadAsync(response, 1024, CancellationToken.None));

        Assert.Equal("POS_UNSUPPORTED_CONTENT_ENCODING", exception.Code);
    }

    [Fact]
    public void VdatetimeMapper_MapsNamespaceIndependentValues()
    {
        string xml = Fixture("vdatetime-success.xml");
        var mapper = new VdatetimeXmlMapper();
        var fetched = new DateTimeOffset(2026, 7, 31, 8, 20, 1, TimeSpan.Zero);

        VdatetimeResult result = mapper.Parse(xml, fetched);

        Assert.Equal("6720", result.SiteId);
        Assert.Equal("US/Eastern", result.SystemTimeZoneId);
        Assert.Equal(-300, result.TimeZones[0].OffsetMinutes);
        Assert.True(result.TimeZones[0].DstApplies);
        Assert.Equal("US/Arizona", result.TimeZones[1].TimeZoneId);
        Assert.Equal(xml, result.RawXml);
        Assert.Equal(fetched, result.FetchedAtUtc);
    }

    [Fact]
    public void VdatetimeMapper_RejectsInvalidOffset()
    {
        var mapper = new VdatetimeXmlMapper();

        PosResponseException exception = Assert.Throws<PosResponseException>(
            () => mapper.Parse(
                Fixture("vdatetime-invalid-offset.xml"),
                DateTimeOffset.UtcNow));

        Assert.Equal("POS_INVALID_RESPONSE", exception.Code);
    }

    [Fact]
    public async Task Authentication_ExtractsXmlCookieAndSite()
    {
        var handler = new FakeHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    Fixture("login-success.xml"),
                    Encoding.UTF8,
                    "text/xml"),
            });
        using var client = new HttpClient(handler);
        var service = new PosAuthenticationService(
            client,
            new PosHttpRequestFactory(new PosOptions()),
            new PosResponseReader(),
            new PosOptions(),
            TimeProvider.System);

        PosSession session = await service.LoginAsync(
            CreateSettings(),
            CancellationToken.None);

        Assert.Equal("FAKE_COOKIE_FOR_TESTS_ONLY", session.Cookie);
        Assert.Equal("6720", session.SiteId);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task PosData_HttpUnauthorizedMapsToSessionExpiry()
    {
        var handler = new FakeHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("<error />"),
            });
        using var client = new HttpClient(handler);
        var service = new PosDataService(
            client,
            new PosHttpRequestFactory(new PosOptions()),
            new PosResponseReader(),
            new VdatetimeXmlMapper(),
            new PosOptions(),
            TimeProvider.System);

        PosAuthenticationException exception =
            await Assert.ThrowsAsync<PosAuthenticationException>(
                () => service.GetVdatetimeAsync(
                    CreateSettings(),
                    "FAKE_COOKIE",
                    CancellationToken.None));

        Assert.Equal("POS_AUTH_EXPIRED", exception.Code);
    }

    [Fact]
    public async Task PosData_LoginRequiredFaultMapsToSessionExpiry()
    {
        var handler = new FakeHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    Fixture("pos-login-required.xml"),
                    Encoding.UTF8,
                    "text/xml"),
            });
        using var client = new HttpClient(handler);
        var options = new PosOptions();
        var service = new PosDataService(
            client,
            new PosHttpRequestFactory(options),
            new PosResponseReader(),
            new VdatetimeXmlMapper(),
            options,
            TimeProvider.System);

        PosAuthenticationException exception =
            await Assert.ThrowsAsync<PosAuthenticationException>(
                () => service.GetVdatetimeAsync(
                    CreateSettings(),
                    "FAKE_EXPIRED_COOKIE",
                    CancellationToken.None));

        Assert.Equal("POS_AUTH_EXPIRED", exception.Code);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task PosData_UnrelatedFaultRemainsInvalidResponse()
    {
        const string xml =
            """
            <VFI:Response xmlns:VFI="urn:vfi-sapphire:np.domain.2001-07-01">
              <VFI:Fault>
                <faultCode>CGIPortal.InvalidCommand</faultCode>
                <faultString>Command unavailable</faultString>
              </VFI:Fault>
            </VFI:Response>
            """;
        var handler = new FakeHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    xml,
                    Encoding.UTF8,
                    "text/xml"),
            });
        using var client = new HttpClient(handler);
        var options = new PosOptions();
        var service = new PosDataService(
            client,
            new PosHttpRequestFactory(options),
            new PosResponseReader(),
            new VdatetimeXmlMapper(),
            options,
            TimeProvider.System);

        PosResponseException exception =
            await Assert.ThrowsAsync<PosResponseException>(
                () => service.GetVdatetimeAsync(
                    CreateSettings(),
                    "FAKE_COOKIE",
                    CancellationToken.None));

        Assert.Equal("POS_INVALID_RESPONSE", exception.Code);
    }

    [Fact]
    public async Task PosData_GetPluPageSendsPageRequestAndMapsFixture()
    {
        var transport = new RecordingPosHttpClient(
            CreateResponse(Fixture("plu-page-success.xml")));
        var fetchedAt = new DateTimeOffset(
            2026,
            7,
            31,
            9,
            15,
            0,
            TimeSpan.Zero);
        var service = CreateDataService(transport, fetchedAt);

        PluPageResult result = await service.GetPluPageAsync(
            CreateSettings(),
            "FAKE_COOKIE",
            new PluPageQuery(2, 25),
            CancellationToken.None);

        Assert.Equal("vPLUs", transport.Command);
        Assert.Equal("cmd=vPLUs&cookie=FAKE_COOKIE", transport.Query);
        Assert.Contains("<pageSize>25</pageSize><page>2</page>", transport.Body);
        Assert.Equal(2, result.Page);
        Assert.Equal(2, result.ItemCount);
        Assert.Equal("FAKE PRODUCT A", result.Products[0].Description);
        Assert.Equal(fetchedAt, result.FetchedAtUtc);
    }

    [Fact]
    public async Task PosData_GetPluSendsKeyboardLookupAndMapsFixture()
    {
        const string xml =
            "<PLUs><PLU><upc>00000000000001</upc>" +
            "<upcModifier>000</upcModifier>" +
            "<description>FAKE PRODUCT LOOKUP</description>" +
            "<department>10</department></PLU></PLUs>";
        var transport = new RecordingPosHttpClient(CreateResponse(xml));
        var fetchedAt = new DateTimeOffset(
            2026,
            7,
            31,
            9,
            16,
            0,
            TimeSpan.Zero);
        var service = CreateDataService(transport, fetchedAt);

        PluLookupResult result = await service.GetPluAsync(
            CreateSettings(),
            "FAKE_COOKIE",
            new PluLookupQuery("00000000000001", "000"),
            CancellationToken.None);

        Assert.Equal("vPLUs", transport.Command);
        Assert.Equal("cmd=vPLUs&cookie=FAKE_COOKIE", transport.Query);
        Assert.Contains("upc source=\"keyboard\"", transport.Body);
        Assert.Contains("<upcModifier>000</upcModifier>", transport.Body);
        Assert.True(result.Found);
        Assert.Equal("FAKE PRODUCT LOOKUP", result.Product!.Description);
        Assert.Equal(fetchedAt, result.FetchedAtUtc);
    }

    [Fact]
    public async Task PosData_GetReferentialIntegritySendsFixedRequestAndMapsFixture()
    {
        var transport = new RecordingPosHttpClient(
            CreateResponse(Fixture("referential-integrity-success.xml")));
        var fetchedAt = new DateTimeOffset(
            2026,
            7,
            31,
            9,
            17,
            0,
            TimeSpan.Zero);
        var service = CreateDataService(transport, fetchedAt);

        ReferentialIntegrityResult result =
            await service.GetReferentialIntegrityAsync(
                CreateSettings(),
                "FAKE_COOKIE",
                CancellationToken.None);

        Assert.Equal("vrefinteg", transport.Command);
        Assert.Equal(
            "cmd=vrefinteg&dataset=prodCodes,departments,ageValidations,taxRates,blueLaws,fees&cookie=FAKE_COOKIE",
            transport.Query);
        Assert.Equal(transport.Query, transport.Body);
        Assert.Equal("FAKE-SITE-17", result.SiteId);
        Assert.Equal(2, result.Departments.Count);
        Assert.Equal(fetchedAt, result.FetchedAtUtc);
    }

    [Theory]
    [InlineData("page", 401)]
    [InlineData("page", 403)]
    [InlineData("lookup", 401)]
    [InlineData("lookup", 403)]
    [InlineData("referential", 401)]
    [InlineData("referential", 403)]
    public async Task PosData_NewOperationsMapAuthenticationHttpStatus(
        string operation,
        int statusCode)
    {
        var transport = new RecordingPosHttpClient(
            CreateResponse("<error />", statusCode));
        var service = CreateDataService(transport);

        PosAuthenticationException exception =
            await Assert.ThrowsAsync<PosAuthenticationException>(
                () => InvokeNewOperationAsync(service, operation));

        Assert.Equal("POS_AUTH_EXPIRED", exception.Code);
        Assert.Equal(1, transport.CallCount);
    }

    [Theory]
    [InlineData("page")]
    [InlineData("lookup")]
    [InlineData("referential")]
    public async Task PosData_NewOperationsMapLoginRequiredFault(
        string operation)
    {
        var transport = new RecordingPosHttpClient(
            CreateResponse(Fixture("pos-login-required.xml")));
        var service = CreateDataService(transport);

        PosAuthenticationException exception =
            await Assert.ThrowsAsync<PosAuthenticationException>(
                () => InvokeNewOperationAsync(service, operation));

        Assert.Equal("POS_AUTH_EXPIRED", exception.Code);
    }

    [Theory]
    [InlineData("page")]
    [InlineData("lookup")]
    [InlineData("referential")]
    public async Task PosData_NewOperationsRetainUnrelatedFault(
        string operation)
    {
        const string xml =
            """
            <VFI:Response xmlns:VFI="urn:vfi-sapphire:np.domain.2001-07-01">
              <VFI:Fault>
                <faultCode>CGIPortal.InvalidCommand</faultCode>
                <faultString>Command unavailable</faultString>
              </VFI:Fault>
            </VFI:Response>
            """;
        var transport = new RecordingPosHttpClient(CreateResponse(xml));
        var service = CreateDataService(transport);

        PosResponseException exception =
            await Assert.ThrowsAsync<PosResponseException>(
                () => InvokeNewOperationAsync(service, operation));

        Assert.Equal("POS_INVALID_RESPONSE", exception.Code);
    }

    [Theory]
    [InlineData("page", "cancelled")]
    [InlineData("lookup", "cancelled")]
    [InlineData("referential", "cancelled")]
    [InlineData("page", "response-limit")]
    [InlineData("lookup", "response-limit")]
    [InlineData("referential", "response-limit")]
    public async Task PosData_NewOperationsPropagateTransportFailures(
        string operation,
        string failure)
    {
        Exception expected = failure == "cancelled"
            ? new OperationCanceledException(CancellationToken.None)
            : new PosResponseException(
                "POS_INVALID_RESPONSE",
                "The POS response exceeded the configured size limit.");
        var transport = new RecordingPosHttpClient(expected);
        var service = CreateDataService(transport);

        Exception actual = await Assert.ThrowsAnyAsync<Exception>(
            () => InvokeNewOperationAsync(service, operation));

        Assert.Same(expected, actual);
    }

    private static ConnectorSettings CreateSettings() =>
        new()
        {
            PosBaseUrl = "https://pos.example.test",
            PosUsername = "FAKE_USER",
            PosPassword = "FAKE_PASSWORD",
            LicenseKey = "FAKE-LICENSE-001",
        };

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static PosDataService CreateDataService(
        IPosHttpClient transport,
        DateTimeOffset? fetchedAt = null) =>
        new(
            transport,
            new PosHttpRequestFactory(new PosOptions()),
            new VdatetimeXmlMapper(),
            new PluXmlMapper(),
            new ReferentialIntegrityXmlMapper(),
            new FixedTimeProvider(
                fetchedAt ?? new DateTimeOffset(
                    2026,
                    7,
                    31,
                    9,
                    0,
                    0,
                    TimeSpan.Zero)));

    private static PosHttpResponse CreateResponse(
        string body,
        int statusCode = 200) =>
        new()
        {
            Metadata = new PosResponseMetadata
            {
                StatusCode = statusCode,
            },
            Body = body,
        };

    private static async Task<object> InvokeNewOperationAsync(
        PosDataService service,
        string operation) => operation switch
        {
            "page" => await service.GetPluPageAsync(
                CreateSettings(),
                "FAKE_COOKIE",
                new PluPageQuery(2, 25),
                CancellationToken.None),
            "lookup" => await service.GetPluAsync(
                CreateSettings(),
                "FAKE_COOKIE",
                new PluLookupQuery("00000000000001", "000"),
                CancellationToken.None),
            "referential" => await service.GetReferentialIntegrityAsync(
                CreateSettings(),
                "FAKE_COOKIE",
                CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

    private static void AssertRequestCompatibility(
        HttpRequestMessage request,
        string command,
        string body)
    {
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(HttpVersion.Version11, request.Version);
        Assert.Equal("pos.example.test", request.Headers.Host);
        Assert.Equal(
            PosCompatibilityHeaders.UserAgent,
            request.Headers.UserAgent.ToString());
        Assert.Equal(
            PosCompatibilityHeaders.AcceptEncoding,
            string.Join(", ", request.Headers.GetValues("Accept-Encoding")));
        Assert.Equal(
            PosCompatibilityHeaders.AcceptLanguage,
            request.Headers.NonValidated["Accept-Language"].ToString());
        Assert.Equal("https://pos.example.test", request.Headers.GetValues("Origin").Single());
        Assert.Equal("https://pos.example.test/ConfigClient.html", request.Headers.Referrer!.ToString());
        Assert.Equal("keep-alive", request.Headers.Connection.Single());
        Assert.Equal(
            PosCompatibilityHeaders.SecFetchDest,
            request.Headers.GetValues("Sec-Fetch-Dest").Single());
        Assert.Equal(
            PosCompatibilityHeaders.SecFetchMode,
            request.Headers.GetValues("Sec-Fetch-Mode").Single());
        Assert.Equal(
            PosCompatibilityHeaders.SecFetchSite,
            request.Headers.GetValues("Sec-Fetch-Site").Single());
        Assert.Equal(
            PosCompatibilityHeaders.SecChUa,
            request.Headers.GetValues("sec-ch-ua").Single());
        Assert.Equal(
            PosCompatibilityHeaders.SecChUaMobile,
            request.Headers.GetValues("sec-ch-ua-mobile").Single());
        Assert.Equal(
            PosCompatibilityHeaders.SecChUaPlatform,
            request.Headers.GetValues("sec-ch-ua-platform").Single());
        HttpContent content = Assert.IsAssignableFrom<HttpContent>(request.Content);
        Assert.Equal("text/plain; charset=UTF-8", content.Headers.ContentType!.ToString());
        Assert.Equal(Encoding.UTF8.GetByteCount(body), content.Headers.ContentLength);
        Assert.DoesNotContain("Accept", request.Headers.Select(header => header.Key));
        Assert.True(request.Options.TryGetValue(
            PosHttpRequestFactory.CommandKey,
            out string? actualCommand));
        Assert.Equal(command, actualCommand);
    }

    private sealed class FakeHttpMessageHandler(HttpResponseMessage response)
        : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(response);
        }
    }

    private sealed class RecordingPosHttpClient : IPosHttpClient
    {
        private readonly PosHttpResponse? _response;
        private readonly Exception? _exception;

        public RecordingPosHttpClient(PosHttpResponse response)
        {
            _response = response;
        }

        public RecordingPosHttpClient(Exception exception)
        {
            _exception = exception;
        }

        public int CallCount { get; private set; }
        public string Command { get; private set; } = string.Empty;
        public string Query { get; private set; } = string.Empty;
        public string Body { get; private set; } = string.Empty;

        public async Task<PosHttpResponse> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            request.Options.TryGetValue(
                PosHttpRequestFactory.CommandKey,
                out string? command);
            Command = command ?? string.Empty;
            Query = request.RequestUri!.Query.TrimStart('?');
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (_exception is not null)
            {
                throw _exception;
            }

            return _response!;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
