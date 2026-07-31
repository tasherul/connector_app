using System.Globalization;
using System.Xml.Linq;
using RetwhoConnector.Core.Exceptions;
using RetwhoConnector.Core.Models;

namespace RetwhoConnector.Core.Services;

public sealed class PluXmlMapper
{
    public PluPageResult ParsePage(
        string xml,
        PluPageQuery query,
        DateTimeOffset fetchedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(query);

        XElement root = ParseRoot(xml);
        IReadOnlyList<PluProduct> products = ParseProducts(root);

        return new PluPageResult
        {
            Page = ParsePositiveInteger(RequiredAttributeValue(root, "page")),
            TotalPages = ParsePositiveInteger(RequiredAttributeValue(root, "ofPages")),
            RequestedPageSize = query.PageSize,
            ItemCount = products.Count,
            Products = products,
            FetchedAtUtc = fetchedAtUtc.ToUniversalTime(),
        };
    }

    public PluLookupResult ParseLookup(
        string xml,
        PluLookupQuery query,
        DateTimeOffset fetchedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<PluProduct> products = ParseProducts(ParseRoot(xml));
        if (products.Count > 1)
        {
            throw Invalid("The POS lookup response contains multiple products.");
        }

        return new PluLookupResult
        {
            RequestedUpc = query.Upc,
            RequestedUpcModifier = query.UpcModifier,
            Found = products.Count == 1,
            Product = products.Count == 1 ? products[0] : null,
            FetchedAtUtc = fetchedAtUtc.ToUniversalTime(),
        };
    }

    private static XElement ParseRoot(string xml)
    {
        XDocument document = SecureXml.Parse(xml);
        XElement root = document.Root
            ?? throw Invalid("The POS response has no root element.");
        if (!IsNamed(root, "PLUs"))
        {
            throw Invalid("The POS returned an unexpected XML document.");
        }

        return root;
    }

    private static IReadOnlyList<PluProduct> ParseProducts(XElement root) => root
        .Elements()
        .Where(element => IsNamed(element, "PLU"))
        .Select(ParseProduct)
        .ToArray();

    private static PluProduct ParseProduct(XElement product) => new()
    {
        Upc = RequiredChildValue(product, "upc"),
        UpcModifier = RequiredChildValue(product, "upcModifier"),
        Description = RequiredChildValue(product, "description"),
        DepartmentId = RequiredChildValue(product, "department"),
        FeeIds = ValuesInContainer(product, "fees", "fee"),
        ProductCode = OptionalChildValue(product, "pcode"),
        Price = OptionalDecimal(product, "price"),
        FlagIds = AttributeValuesInContainer(product, "flags", "flag", "sysid"),
        TaxRateIds = AttributeValuesInContainer(product, "taxRates", "taxRate", "sysid"),
        IdCheckIds = AttributeValuesInContainer(product, "idChecks", "idCheck", "sysid"),
        SellUnit = OptionalDecimal(product, "SellUnit"),
        TaxableRebateAmount = OptionalNestedDecimal(product, "taxableRebate", "amount"),
        GroupCodes = ParseGroupCodes(product),
        MaxQuantityPerTransaction = OptionalDecimal(product, "maxQtyPerTrans"),
    };

    private static IReadOnlyList<string> ValuesInContainer(
        XElement product,
        string containerName,
        string valueName) => Children(product, containerName)
        .SelectMany(container => Children(container, valueName))
        .Select(element => RequiredElementValue(element, "array"))
        .ToArray();

    private static IReadOnlyList<string> AttributeValuesInContainer(
        XElement product,
        string containerName,
        string elementName,
        string attributeName) => Children(product, containerName)
        .SelectMany(container => Children(container, elementName))
        .Select(element => RequiredAttributeValue(element, attributeName))
        .ToArray();

    private static IReadOnlyList<IndexedCode> ParseGroupCodes(XElement product) => Children(
            product,
            "groupCode")
        .Select(element => new IndexedCode
        {
            Index = ParseNonNegativeInteger(AttributeValue(element, "index")),
            Code = RequiredElementValue(element, "group code"),
        })
        .ToArray();

    private static decimal? OptionalDecimal(XElement parent, string childName)
    {
        XElement? child = SingleChild(parent, childName);
        return child is null ? null : ParseDecimal(RequiredElementValue(child, "numeric"));
    }

    private static decimal? OptionalNestedDecimal(
        XElement parent,
        string containerName,
        string childName)
    {
        XElement? container = SingleChild(parent, containerName);
        XElement? child = container is null ? null : SingleChild(container, childName);
        if (container is not null && child is null)
        {
            throw Invalid("The POS response contains invalid numeric data.");
        }

        return child is null ? null : ParseDecimal(RequiredElementValue(child, "numeric"));
    }

    private static string RequiredChildValue(XElement parent, string childName)
    {
        XElement? child = SingleChild(parent, childName);
        return child is null
            ? throw Invalid("The POS response is missing required product data.")
            : RequiredElementValue(child, "product data");
    }

    private static string? OptionalChildValue(XElement parent, string childName)
    {
        XElement? child = SingleChild(parent, childName);
        return child is null ? null : RequiredElementValue(child, "product data");
    }

    private static string RequiredAttributeValue(XElement element, string attributeName)
    {
        string? value = AttributeValue(element, attributeName)?.Trim();
        return string.IsNullOrEmpty(value)
            ? throw Invalid("The POS response contains invalid product data.")
            : value;
    }

    private static string RequiredElementValue(XElement element, string dataName)
    {
        string value = element.Value.Trim();
        return string.IsNullOrEmpty(value)
            ? throw Invalid($"The POS response contains invalid {dataName}.")
            : value;
    }

    private static int ParsePositiveInteger(string? text)
    {
        if (!int.TryParse(
            text,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out int value) || value <= 0)
        {
            throw Invalid("The POS response contains invalid page metadata.");
        }

        return value;
    }

    private static int ParseNonNegativeInteger(string? text)
    {
        if (!int.TryParse(
            text,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out int value) || value < 0)
        {
            throw Invalid("The POS response contains an invalid group code index.");
        }

        return value;
    }

    private static decimal ParseDecimal(string text)
    {
        if (!decimal.TryParse(
            text,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out decimal value))
        {
            throw Invalid("The POS response contains an invalid numeric value.");
        }

        return value;
    }

    private static IEnumerable<XElement> Children(XElement parent, string localName) => parent
        .Elements()
        .Where(element => IsNamed(element, localName));

    private static XElement? SingleChild(XElement parent, string localName)
    {
        XElement[] children = Children(parent, localName).ToArray();
        return children.Length switch
        {
            0 => null,
            1 => children[0],
            _ => throw Invalid("The POS response contains ambiguous product data."),
        };
    }

    private static string? AttributeValue(XElement element, string localName)
    {
        XAttribute[] attributes = element
            .Attributes()
            .Where(attribute => attribute.Name.LocalName.Equals(
                localName,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return attributes.Length switch
        {
            0 => null,
            1 => attributes[0].Value,
            _ => throw Invalid("The POS response contains ambiguous product data."),
        };
    }

    private static bool IsNamed(XElement element, string localName) =>
        element.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase);

    private static PosResponseException Invalid(string message) =>
        new("POS_INVALID_RESPONSE", message);
}
