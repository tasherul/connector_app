using System.Text.Json;
using RetwhoConnector.Core.Models;
using RetwhoConnector.Core.Serialization;
using RetwhoConnector.Core.Validation;

namespace RetwhoConnector.Tests;

public sealed class ConnectorContractTests
{
    [Fact]
    public void SettingsValidation_NormalizesHttpsOrigin()
    {
        ConnectorSettings input = CreateSettings("https://pos.example.test:443/");

        ConnectorSettings result = ConnectorSettingsValidator.Validate(input);

        Assert.Equal("https://pos.example.test", result.PosBaseUrl);
    }

    [Theory]
    [InlineData("http://pos.example.test")]
    [InlineData("https://user@pos.example.test")]
    [InlineData("https://pos.example.test/path")]
    [InlineData("https://pos.example.test?query=value")]
    [InlineData("https://pos.example.test/#fragment")]
    public void SettingsValidation_RejectsUnsafePosUrls(string posBaseUrl)
    {
        Assert.Throws<ArgumentException>(
            () => ConnectorSettingsValidator.Validate(CreateSettings(posBaseUrl)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("contains space")]
    [InlineData("bad/slash")]
    public void SettingsValidation_RejectsInvalidLicense(string licenseKey)
    {
        ConnectorSettings input = CreateSettings() with { LicenseKey = licenseKey };

        Assert.Throws<ArgumentException>(
            () => ConnectorSettingsValidator.Validate(input));
    }

    [Fact]
    public void Acknowledgement_UsesCamelCaseAndOmitsNulls()
    {
        BridgeAcknowledgement acknowledgement =
            BridgeAcknowledgement.Success(new { Value = 42 });

        string json = JsonSerializer.Serialize(
            acknowledgement,
            ConnectorJson.Options);

        Assert.Equal("""{"ok":true,"result":{"value":42}}""", json);
    }

    [Fact]
    public void PluPageParameters_ApplyDefaultsAndReadSuppliedValues()
    {
        using JsonDocument defaults = JsonDocument.Parse("{}");
        Assert.Equal(
            new PluPageQuery(1, 100),
            BridgeActionParameterReader.ReadPluPage(defaults.RootElement));

        using JsonDocument supplied =
            JsonDocument.Parse("""{"page":2,"pageSize":25}""");
        Assert.Equal(
            new PluPageQuery(2, 25),
            BridgeActionParameterReader.ReadPluPage(supplied.RootElement));
    }

    [Theory]
    [InlineData("""{"page":0}""", "page must be an integer between 1 and 2147483647.")]
    [InlineData("""{"page":1.5}""", "page must be an integer between 1 and 2147483647.")]
    [InlineData("""{"pageSize":0}""", "pageSize must be an integer between 1 and 100.")]
    [InlineData("""{"pageSize":101}""", "pageSize must be an integer between 1 and 100.")]
    [InlineData("""{"unexpected":true}""", "Parameters contain an unsupported property.")]
    public void PluPageParameters_RejectInvalidValues(string json, string message)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => BridgeActionParameterReader.ReadPluPage(document.RootElement));

        Assert.Equal(message, exception.Message);
    }

    [Fact]
    public void PluLookupParameters_DefaultModifier()
    {
        using JsonDocument lookup =
            JsonDocument.Parse("""{"upc":"00000000000001"}""");

        Assert.Equal(
            new PluLookupQuery("00000000000001", "000"),
            BridgeActionParameterReader.ReadPluLookup(lookup.RootElement));
    }

    [Theory]
    [InlineData("{}", "upc is required.")]
    [InlineData("""{"upc":""}""", "upc must contain 1 to 32 digits.")]
    [InlineData("""{"upc":"ABC"}""", "upc must contain 1 to 32 digits.")]
    [InlineData("""{"upc":"123456789012345678901234567890123"}""", "upc must contain 1 to 32 digits.")]
    [InlineData("""{"upc":"00000000000001","upcModifier":"00"}""", "upcModifier must contain exactly 3 digits.")]
    [InlineData("""{"upc":"00000000000001","upcModifier":"0A0"}""", "upcModifier must contain exactly 3 digits.")]
    [InlineData("""{"upc":"00000000000001","unexpected":true}""", "Parameters contain an unsupported property.")]
    public void PluLookupParameters_RejectInvalidValues(string json, string message)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => BridgeActionParameterReader.ReadPluLookup(document.RootElement));

        Assert.Equal(message, exception.Message);
    }

    [Fact]
    public void PluResults_UseCamelCaseNumbersAndOmitNullProduct()
    {
        DateTimeOffset fetchedAtUtc = new(2026, 7, 31, 0, 0, 0, TimeSpan.Zero);
        var product = new PluProduct
        {
            Upc = "00000000000001",
            UpcModifier = "000",
            Description = "FAKE PRODUCT",
            DepartmentId = "19",
            FeeIds = ["0"],
            ProductCode = "0",
            Price = 4.67m,
            TaxRateIds = ["1"],
            IdCheckIds = ["2"],
            SellUnit = 1.000m,
            TaxableRebateAmount = 0.00m,
            GroupCodes = [new IndexedCode { Index = 0, Code = "5" }],
            MaxQuantityPerTransaction = 2.00m,
        };
        var page = new PluPageResult
        {
            Page = 1,
            TotalPages = 4,
            RequestedPageSize = 100,
            ItemCount = 1,
            Products = [product],
            FetchedAtUtc = fetchedAtUtc,
        };
        var lookup = new PluLookupResult
        {
            RequestedUpc = "00000000000002",
            RequestedUpcModifier = "000",
            Found = false,
            Product = null,
            FetchedAtUtc = fetchedAtUtc,
        };

        Assert.Equal(
            """{"source":"NAXML","command":"vPLUs","page":1,"totalPages":4,"requestedPageSize":100,"itemCount":1,"products":[{"upc":"00000000000001","upcModifier":"000","description":"FAKE PRODUCT","departmentId":"19","feeIds":["0"],"productCode":"0","price":4.67,"flagIds":[],"taxRateIds":["1"],"idCheckIds":["2"],"sellUnit":1.000,"taxableRebateAmount":0.00,"groupCodes":[{"index":0,"code":"5"}],"maxQuantityPerTransaction":2.00}],"fetchedAtUtc":"2026-07-31T00:00:00+00:00"}""",
            JsonSerializer.Serialize(page, ConnectorJson.Options));
        Assert.Equal(
            """{"source":"NAXML","command":"vPLU","requestedUpc":"00000000000002","requestedUpcModifier":"000","found":false,"fetchedAtUtc":"2026-07-31T00:00:00+00:00"}""",
            JsonSerializer.Serialize(lookup, ConnectorJson.Options));
    }

    [Fact]
    public void NewCommandAcknowledgements_ContainNormalizedTypedResults()
    {
        DateTimeOffset fetchedAtUtc = new(2026, 7, 31, 0, 0, 0, TimeSpan.Zero);
        BridgeAcknowledgement page = BridgeAcknowledgement.Success(
            new PluPageResult
            {
                Page = 2,
                TotalPages = 4,
                RequestedPageSize = 25,
                ItemCount = 0,
                FetchedAtUtc = fetchedAtUtc,
            });
        BridgeAcknowledgement lookup = BridgeAcknowledgement.Success(
            new PluLookupResult
            {
                RequestedUpc = "00000000000001",
                RequestedUpcModifier = "000",
                Found = false,
                FetchedAtUtc = fetchedAtUtc,
            });
        BridgeAcknowledgement referential = BridgeAcknowledgement.Success(
            new ReferentialIntegrityResult
            {
                SiteId = "6720",
                Limits = new ReferentialIntegrityLimits
                {
                    TaxRates = new ReferentialDatasetLimits { MaxRecords = 10 },
                    Departments = new ReferentialDatasetLimits { MaxRecords = 20 },
                    ProdCodes = new ReferentialDatasetLimits { MaxRecords = 30 },
                    AgeValidations = new ReferentialDatasetLimits { MaxRecords = 8 },
                    BlueLaws = new ReferentialDatasetLimits { MaxRecords = 0 },
                    Fees = new ReferentialDatasetLimits
                    {
                        MaxRecords = 25,
                        MaxFeesPerItem = 3,
                    },
                },
                FetchedAtUtc = fetchedAtUtc,
            });

        Assert.Equal(
            """{"ok":true,"result":{"source":"NAXML","command":"vPLUs","page":2,"totalPages":4,"requestedPageSize":25,"itemCount":0,"products":[],"fetchedAtUtc":"2026-07-31T00:00:00+00:00"}}""",
            JsonSerializer.Serialize(page, ConnectorJson.Options));
        Assert.Equal(
            """{"ok":true,"result":{"source":"NAXML","command":"vPLU","requestedUpc":"00000000000001","requestedUpcModifier":"000","found":false,"fetchedAtUtc":"2026-07-31T00:00:00+00:00"}}""",
            JsonSerializer.Serialize(lookup, ConnectorJson.Options));
        Assert.Equal(
            """{"ok":true,"result":{"source":"NAXML","command":"vrefinteg","siteId":"6720","limits":{"taxRates":{"maxRecords":10},"departments":{"maxRecords":20},"prodCodes":{"maxRecords":30},"ageValidations":{"maxRecords":8},"blueLaws":{"maxRecords":0},"fees":{"maxRecords":25,"maxFeesPerItem":3}},"taxRates":[],"departments":[],"productCodes":[],"ageValidations":[],"fees":[],"blueLaws":[],"fetchedAtUtc":"2026-07-31T00:00:00+00:00"}}""",
            JsonSerializer.Serialize(referential, ConnectorJson.Options));
    }

    [Fact]
    public void ActionValidation_RequiresObjectParams()
    {
        using JsonDocument document = JsonDocument.Parse("[]");
        var action = new BridgeAction
        {
            ActionId = "action-1",
            Command = "get_current_data",
            Params = document.RootElement.Clone(),
            Timestamp = DateTimeOffset.UtcNow,
        };

        Assert.Throws<ArgumentException>(
            () => BridgeActionValidator.Validate(action));
    }

    [Fact]
    public void ActionValidation_BoundsActionId()
    {
        using JsonDocument document = JsonDocument.Parse("{}");
        var action = new BridgeAction
        {
            ActionId = new string('a', 129),
            Command = "get_current_data",
            Params = document.RootElement.Clone(),
            Timestamp = DateTimeOffset.UtcNow,
        };

        Assert.Throws<ArgumentException>(
            () => BridgeActionValidator.Validate(action));
    }

    [Fact]
    public async Task ActionContext_AcknowledgesExactlyOnce()
    {
        using JsonDocument document = JsonDocument.Parse("{}");
        var action = new BridgeAction
        {
            ActionId = "action-1",
            Command = "get_current_data",
            Params = document.RootElement.Clone(),
            Timestamp = DateTimeOffset.UtcNow,
        };
        var calls = 0;
        var context = new BridgeActionContext(
            action,
            (_, _) =>
            {
                calls++;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        await context.AcknowledgeOnceAsync(
            BridgeAcknowledgement.Success(new { value = 1 }));
        await context.AcknowledgeOnceAsync(
            BridgeAcknowledgement.Failure("must not be sent"));

        Assert.Equal(1, calls);
        Assert.True(context.IsAcknowledged);
    }

    private static ConnectorSettings CreateSettings(
        string posBaseUrl = "https://pos.example.test") =>
        new()
        {
            PosBaseUrl = posBaseUrl,
            PosUsername = "FAKE_USER",
            PosPassword = "FAKE_PASSWORD",
            LicenseKey = "FAKE-LICENSE-001",
            AutoConnect = true,
        };
}
