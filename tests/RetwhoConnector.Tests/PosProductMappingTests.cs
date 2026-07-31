using RetwhoConnector.Core.Exceptions;
using RetwhoConnector.Core.Models;
using RetwhoConnector.Core.Services;

namespace RetwhoConnector.Tests;

public sealed class PosProductMappingTests
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
    public void ParsePage_NormalizesEveryProductPropertyAndMetadata()
    {
        PluPageResult result = new PluXmlMapper().ParsePage(
            ReadFixture("plu-page-success.xml"),
            new PluPageQuery(2, 25),
            FetchedAt);

        Assert.Equal("NAXML", result.Source);
        Assert.Equal("vPLUs", result.Command);
        Assert.Equal(2, result.Page);
        Assert.Equal(4, result.TotalPages);
        Assert.Equal(25, result.RequestedPageSize);
        Assert.Equal(2, result.ItemCount);
        Assert.Equal(FetchedAt.ToUniversalTime(), result.FetchedAtUtc);

        PluProduct first = Assert.Single(result.Products.Take(1));
        Assert.Equal("00000000000001", first.Upc);
        Assert.Equal("000", first.UpcModifier);
        Assert.Equal("FAKE PRODUCT A", first.Description);
        Assert.Equal("10", first.DepartmentId);
        Assert.Equal(["0", "7"], first.FeeIds);
        Assert.Equal("400", first.ProductCode);
        Assert.Equal(4.67m, first.Price);
        Assert.Equal(["1", "9"], first.FlagIds);
        Assert.Equal(["2"], first.TaxRateIds);
        Assert.Equal(["3"], first.IdCheckIds);
        Assert.Equal(1.000m, first.SellUnit);
        Assert.Equal(0.00m, first.TaxableRebateAmount);
        Assert.Collection(
            first.GroupCodes,
            code => Assert.Equal(new IndexedCode { Index = 0, Code = "5" }, code),
            code => Assert.Equal(new IndexedCode { Index = 2, Code = "7" }, code));
        Assert.Equal(2.00m, first.MaxQuantityPerTransaction);

        PluProduct second = result.Products[1];
        Assert.Equal("00000000000002", second.Upc);
        Assert.Equal("123", second.UpcModifier);
        Assert.Equal("FAKE PRODUCT B", second.Description);
        Assert.Equal("11", second.DepartmentId);
        Assert.Empty(second.FeeIds);
        Assert.Null(second.ProductCode);
        Assert.Null(second.Price);
        Assert.Empty(second.FlagIds);
        Assert.Empty(second.TaxRateIds);
        Assert.Empty(second.IdCheckIds);
        Assert.Null(second.SellUnit);
        Assert.Null(second.TaxableRebateAmount);
        Assert.Empty(second.GroupCodes);
        Assert.Null(second.MaxQuantityPerTransaction);
    }

    [Fact]
    public void ParseLookup_ReturnsFoundProductAndRequestedIdentifier()
    {
        PluLookupQuery query = new("00000000000001", "000");

        PluLookupResult result = new PluXmlMapper().ParseLookup(
            "<PLUs><PLU><upc>00000000000001</upc><upcModifier>000</upcModifier><description>FAKE PRODUCT A</description><department>10</department></PLU></PLUs>",
            query,
            FetchedAt);

        Assert.Equal("NAXML", result.Source);
        Assert.Equal("vPLU", result.Command);
        Assert.Equal(query.Upc, result.RequestedUpc);
        Assert.Equal(query.UpcModifier, result.RequestedUpcModifier);
        Assert.True(result.Found);
        PluProduct product = Assert.IsType<PluProduct>(result.Product);
        Assert.Equal("00000000000001", product.Upc);
        Assert.Equal(FetchedAt.ToUniversalTime(), result.FetchedAtUtc);
    }

    [Fact]
    public void ParseLookup_ReturnsNotFoundForEmptyProductList()
    {
        PluLookupQuery query = new("00000000000009", "123");

        PluLookupResult result = new PluXmlMapper().ParseLookup(
            ReadFixture("plu-empty.xml"),
            query,
            FetchedAt);

        Assert.Equal(query.Upc, result.RequestedUpc);
        Assert.Equal(query.UpcModifier, result.RequestedUpcModifier);
        Assert.False(result.Found);
        Assert.Null(result.Product);
        Assert.Equal(FetchedAt.ToUniversalTime(), result.FetchedAtUtc);
    }

    [Fact]
    public void ParseLookup_RejectsMultipleProducts()
    {
        PosResponseException exception = Assert.Throws<PosResponseException>(
            () => new PluXmlMapper().ParseLookup(
                ReadFixture("plu-multiple.xml"),
                new PluLookupQuery("00000000000001", "000"),
                FetchedAt));

        Assert.Equal("POS_INVALID_RESPONSE", exception.Code);
        Assert.Equal("The POS lookup response contains multiple products.", exception.SafeMessage);
    }

    [Theory]
    [InlineData("<notPLUs page=\"1\" ofPages=\"1\" />")]
    [InlineData("<PLUs page=\"0\" ofPages=\"1\" />")]
    [InlineData("<PLUs page=\"1\" ofPages=\"0\" />")]
    [InlineData("<PLUs page=\"1\" ofPages=\"not-an-integer\" />")]
    [InlineData("<PLUs page=\"1\" />")]
    [InlineData("<PLUs page=\"1\" ofPages=\"1\"><PLU><upc>00000000000001</upc><upcModifier>000</upcModifier><description>FAKE</description><department>10</department><price>not-a-decimal</price></PLU></PLUs>")]
    [InlineData("<PLUs page=\"1\" ofPages=\"1\"><PLU><upcModifier>000</upcModifier><description>FAKE</description><department>10</department></PLU></PLUs>")]
    [InlineData("<PLUs page=\"1\" ofPages=\"1\"><PLU><upc>00000000000001</upc><upcModifier>000</upcModifier><description>FAKE</description><department>10</department><groupCode index=\"bad\">5</groupCode></PLU></PLUs>")]
    public void ParsePage_RejectsInvalidResponseStructureOrValues(string xml)
    {
        PosResponseException exception = Assert.Throws<PosResponseException>(
            () => new PluXmlMapper().ParsePage(xml, new PluPageQuery(1, 100), FetchedAt));

        Assert.Equal("POS_INVALID_RESPONSE", exception.Code);
        Assert.DoesNotContain("00000000000001", exception.SafeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ParsePage_RejectsDtd()
    {
        const string xml = "<!DOCTYPE PLUs [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]><PLUs page=\"1\" ofPages=\"1\" />";

        PosResponseException exception = Assert.Throws<PosResponseException>(
            () => new PluXmlMapper().ParsePage(xml, new PluPageQuery(1, 100), FetchedAt));

        Assert.Equal("POS_INVALID_XML", exception.Code);
    }

    private static string ReadFixture(string fileName) => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));
}
