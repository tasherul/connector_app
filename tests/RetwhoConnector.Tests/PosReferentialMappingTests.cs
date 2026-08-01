using RetwhoConnector.Core.Exceptions;
using RetwhoConnector.Core.Models;
using RetwhoConnector.Core.Services;

namespace RetwhoConnector.Tests;

public sealed class PosReferentialMappingTests
{
    private static readonly DateTimeOffset FetchedAt = new(
        2026,
        7,
        31,
        12,
        30,
        0,
        TimeSpan.FromHours(6));

    [Fact]
    public void Parse_MapsFixedDatasetsLimitsAndNormalizedExtensionRecords()
    {
        ReferentialIntegrityResult result = new ReferentialIntegrityXmlMapper().Parse(
            ReadFixture("referential-integrity-success.xml"),
            FetchedAt);

        Assert.Equal("NAXML", result.Source);
        Assert.Equal("vrefinteg", result.Command);
        Assert.Equal("FAKE-SITE-17", result.SiteId);
        Assert.Equal(
            new ReferentialDatasetLimits { MaxRecords = 10 },
            result.Limits.TaxRates);
        Assert.Equal(
            new ReferentialDatasetLimits { MaxRecords = 20 },
            result.Limits.Departments);
        Assert.Equal(
            new ReferentialDatasetLimits { MaxRecords = 30 },
            result.Limits.ProdCodes);
        Assert.Equal(
            new ReferentialDatasetLimits { MaxRecords = 8 },
            result.Limits.AgeValidations);
        Assert.Equal(
            new ReferentialDatasetLimits { MaxRecords = 0 },
            result.Limits.BlueLaws);
        Assert.Equal(
            new ReferentialDatasetLimits
            {
                MaxRecords = 25,
                MaxFeesPerItem = 3,
            },
            result.Limits.Fees);
        Assert.Equal(FetchedAt.ToUniversalTime(), result.FetchedAtUtc);

        NamedReference taxRate = Assert.Single(result.TaxRates);
        Assert.Equal("tax-1", taxRate.Id);
        Assert.Equal("FAKE TAX", taxRate.Name);

        Assert.Collection(
            result.Departments,
            department =>
            {
                Assert.Equal("dept-1", department.Id);
                Assert.Equal("FAKE FUEL", department.Name);
                Assert.True(department.IsFuel);
                Assert.Equal("pc-1", department.ProductCode);
            },
            department =>
            {
                Assert.Equal("dept-2", department.Id);
                Assert.Equal("FAKE GROCERY", department.Name);
                Assert.False(department.IsFuel);
                Assert.Null(department.ProductCode);
            });

        Assert.Collection(
            result.ProductCodes,
            productCode => Assert.Equal(
                new ProductCodeReference
                {
                    Id = "pc-1",
                    Name = "FAKE FUEL CODE",
                    IsFuel = true,
                },
                productCode),
            productCode => Assert.Equal(
                new ProductCodeReference
                {
                    Id = "pc-2",
                    Name = "FAKE GROCERY CODE",
                    IsFuel = false,
                },
                productCode));

        Assert.Collection(
            result.AgeValidations,
            validation => Assert.Equal(
                new NamedReference { Id = "age-1", Name = "FAKE AGE 18" }, validation),
            validation => Assert.Equal(
                new NamedReference { Id = "age-2", Name = "FAKE AGE 21" }, validation));
        Assert.Empty(result.BlueLaws);

        ReferenceDefinition fee = Assert.Single(result.Fees);
        Assert.Equal("fee", fee.RecordType);
        Assert.Equal("fee-1", fee.Id);
        Assert.Equal("FAKE FEE", fee.Name);
        Assert.Equal("flat", fee.Fields["feeType"]);
        Assert.Equal("1.25", fee.Fields["amount"]);
        Assert.Equal(["feeType", "amount"], fee.Fields.Keys);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("True", true)]
    [InlineData("False", false)]
    public void Parse_AcceptsCommonPosBooleanForms(string rawValue, bool expected)
    {
        string xml =
            $$"""
            <referentialIntegrity>
              <site>FAKE-SITE</site>
              <fees maxRecords="1" maxFeesPerItem="1" />
              <departments maxRecords="1">
                <department sysid="dept-1" name="FAKE DEPARTMENT" isFuel="{{rawValue}}" />
              </departments>
              <prodCodes maxRecords="1">
                <prodCode sysid="pc-1" name="FAKE PRODUCT CODE" isFuel="{{rawValue}}" />
              </prodCodes>
            </referentialIntegrity>
            """;

        ReferentialIntegrityResult result =
            new ReferentialIntegrityXmlMapper().Parse(xml, FetchedAt);

        Assert.Equal(expected, Assert.Single(result.Departments).IsFuel);
        Assert.Equal(expected, Assert.Single(result.ProductCodes).IsFuel);
    }

    [Theory]
    [InlineData("<unexpected><site>FAKE-SITE</site><fees maxRecords=\"1\" maxFeesPerItem=\"1\" /></unexpected>")]
    [InlineData("<referentialIntegrity><fees maxRecords=\"1\" maxFeesPerItem=\"1\" /></referentialIntegrity>")]
    [InlineData("<referentialIntegrity><site>FAKE-SITE</site><fees maxRecords=\"one\" maxFeesPerItem=\"1\" /></referentialIntegrity>")]
    [InlineData("<referentialIntegrity><site>FAKE-SITE</site><fees maxRecords=\"1\" maxFeesPerItem=\"many\" /></referentialIntegrity>")]
    [InlineData("<referentialIntegrity><site>FAKE-SITE</site><fees maxRecords=\"1\" maxFeesPerItem=\"1\" /><departments><department sysid=\"dept\" name=\"FAKE\" isFuel=\"maybe\" /></departments></referentialIntegrity>")]
    [InlineData("<referentialIntegrity><site>FAKE-SITE</site><fees maxRecords=\"1\" maxFeesPerItem=\"1\"><fee customValue=\"a\"><CustomValue>b</CustomValue></fee></fees></referentialIntegrity>")]
    [InlineData("<referentialIntegrity><site>FAKE-SITE</site><fees maxRecords=\"1\" maxFeesPerItem=\"1\"><fee><definition><value>nested</value></definition></fee></fees></referentialIntegrity>")]
    [InlineData("<referentialIntegrity><site>FAKE-SITE</site><fees maxRecords=\"1\" maxFeesPerItem=\"1\" /><blueLaws><law><definition><value>nested</value></definition></law></blueLaws></referentialIntegrity>")]
    public void Parse_RejectsInvalidReferentialStructureOrValues(string xml)
    {
        PosResponseException exception = Assert.Throws<PosResponseException>(
            () => new ReferentialIntegrityXmlMapper().Parse(xml, FetchedAt));

        Assert.Equal("POS_INVALID_RESPONSE", exception.Code);
        Assert.DoesNotContain("FAKE-SITE", exception.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsDtd()
    {
        const string xml = "<!DOCTYPE referentialIntegrity [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]><referentialIntegrity><site>FAKE-SITE</site><fees maxRecords=\"1\" maxFeesPerItem=\"1\" /></referentialIntegrity>";

        PosResponseException exception = Assert.Throws<PosResponseException>(
            () => new ReferentialIntegrityXmlMapper().Parse(xml, FetchedAt));

        Assert.Equal("POS_INVALID_XML", exception.Code);
    }

    private static string ReadFixture(string fileName) => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));
}
