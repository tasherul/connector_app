using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using RetwhoConnector.Core.Exceptions;
using RetwhoConnector.Core.Models;

namespace RetwhoConnector.Core.Services;

public sealed class ReferentialIntegrityXmlMapper
{
    public ReferentialIntegrityResult Parse(string xml, DateTimeOffset fetchedAtUtc)
    {
        XElement root = ParseRoot(xml);
        XElement fees = RequiredChild(root, "fees");

        return new ReferentialIntegrityResult
        {
            SiteId = RequiredChildValue(root, "site"),
            Limits = new ReferentialIntegrityLimits
            {
                TaxRates = ParseDatasetLimits(OptionalChild(root, "taxRates")),
                Departments = ParseDatasetLimits(OptionalChild(root, "departments")),
                ProdCodes = ParseDatasetLimits(OptionalChild(root, "prodCodes")),
                AgeValidations = ParseDatasetLimits(
                    OptionalChild(root, "ageValidations")),
                BlueLaws = ParseDatasetLimits(OptionalChild(root, "blueLaws")),
                Fees = ParseDatasetLimits(fees, includeMaxFeesPerItem: true),
            },
            TaxRates = ParseNamedReferences(root, "taxRates", "taxRate"),
            Departments = ParseDepartments(root),
            ProductCodes = ParseProductCodes(root),
            AgeValidations = ParseNamedReferences(root, "ageValidations", "ageValidation"),
            Fees = ParseDefinitions(fees),
            BlueLaws = ParseDefinitions(OptionalChild(root, "blueLaws")),
            FetchedAtUtc = fetchedAtUtc.ToUniversalTime(),
        };
    }

    private static XElement ParseRoot(string xml)
    {
        XDocument document = SecureXml.Parse(xml);
        XElement root = document.Root
            ?? throw Invalid("The POS response has no root element.");
        if (!IsNamed(root, "referentialIntegrity"))
        {
            throw Invalid("The POS returned an unexpected XML document.");
        }

        return root;
    }

    private static IReadOnlyList<NamedReference> ParseNamedReferences(
        XElement root,
        string containerName,
        string recordName) => OptionalChild(root, containerName)?
        .Elements()
        .Where(element => IsNamed(element, recordName))
        .Select(element => new NamedReference
        {
            Id = RequiredAttributeValue(element, "sysid"),
            Name = RequiredAttributeValue(element, "name"),
        })
        .ToArray() ?? [];

    private static IReadOnlyList<DepartmentReference> ParseDepartments(XElement root) =>
        OptionalChild(root, "departments")?
            .Elements()
            .Where(element => IsNamed(element, "department"))
            .Select(element => new DepartmentReference
            {
                Id = RequiredAttributeValue(element, "sysid"),
                Name = RequiredAttributeValue(element, "name"),
                IsFuel = ParseStrictBoolean(RequiredAttributeValue(element, "isFuel")),
                ProductCode = OptionalAttributeValue(element, "prodCode"),
            })
            .ToArray() ?? [];

    private static IReadOnlyList<ProductCodeReference> ParseProductCodes(XElement root) =>
        OptionalChild(root, "prodCodes")?
            .Elements()
            .Where(element => IsNamed(element, "prodCode"))
            .Select(element => new ProductCodeReference
            {
                Id = RequiredAttributeValue(element, "sysid"),
                Name = RequiredAttributeValue(element, "name"),
                IsFuel = ParseStrictBoolean(RequiredAttributeValue(element, "isFuel")),
            })
            .ToArray() ?? [];

    private static IReadOnlyList<ReferenceDefinition> ParseDefinitions(XElement? container) =>
        container is null
            ? []
            : container.Elements().Select(ParseDefinition).ToArray();

    private static ReferenceDefinition ParseDefinition(XElement element)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (XAttribute attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
            {
                continue;
            }

            if (IsNamed(attribute, "sysid") || IsNamed(attribute, "name"))
            {
                continue;
            }

            AddField(fields, attribute.Name.LocalName, attribute.Value);
        }

        foreach (XElement child in element.Elements())
        {
            if (child.HasElements || child.Attributes().Any(
                attribute => !attribute.IsNamespaceDeclaration))
            {
                throw Invalid("The POS response contains unsupported referential data.");
            }

            AddField(fields, child.Name.LocalName, child.Value);
        }

        return new ReferenceDefinition
        {
            RecordType = NormalizeName(element.Name.LocalName),
            Id = OptionalAttributeValue(element, "sysid"),
            Name = OptionalAttributeValue(element, "name"),
            Fields = fields,
        };
    }

    private static void AddField(
        IDictionary<string, string> fields,
        string sourceName,
        string value)
    {
        string normalizedName = NormalizeName(sourceName);
        if (!fields.TryAdd(normalizedName, value.Trim()))
        {
            throw Invalid("The POS response contains duplicate referential data.");
        }
    }

    private static string NormalizeName(string sourceName) =>
        JsonNamingPolicy.CamelCase.ConvertName(sourceName);

    private static bool ParseStrictBoolean(string text) => text.Trim() switch
    {
        "0" => false,
        "1" => true,
        var value when value.Equals("false", StringComparison.OrdinalIgnoreCase) => false,
        var value when value.Equals("true", StringComparison.OrdinalIgnoreCase) => true,
        _ => throw Invalid("The POS response contains an invalid boolean value."),
    };

    private static ReferentialDatasetLimits? ParseDatasetLimits(
        XElement? container,
        bool includeMaxFeesPerItem = false) => container is null
        ? null
        : new ReferentialDatasetLimits
        {
            MaxRecords = ParseNonNegativeInteger(
                RequiredAttributeValue(container, "maxRecords")),
            MaxFeesPerItem = includeMaxFeesPerItem
                ? ParseNonNegativeInteger(
                    RequiredAttributeValue(container, "maxFeesPerItem"))
                : null,
        };

    private static int ParseNonNegativeInteger(string text)
    {
        if (!int.TryParse(
            text,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out int value) || value < 0)
        {
            throw Invalid("The POS response contains invalid referential limits.");
        }

        return value;
    }

    private static string RequiredChildValue(XElement parent, string childName)
    {
        XElement child = RequiredChild(parent, childName);
        string value = child.Value.Trim();
        return string.IsNullOrEmpty(value)
            ? throw Invalid("The POS response is missing required referential data.")
            : value;
    }

    private static XElement RequiredChild(XElement parent, string localName) =>
        OptionalChild(parent, localName)
        ?? throw Invalid("The POS response is missing required referential data.");

    private static XElement? OptionalChild(XElement parent, string localName)
    {
        XElement[] children = parent
            .Elements()
            .Where(element => IsNamed(element, localName))
            .ToArray();
        return children.Length switch
        {
            0 => null,
            1 => children[0],
            _ => throw Invalid("The POS response contains ambiguous referential data."),
        };
    }

    private static string RequiredAttributeValue(XElement element, string localName) =>
        OptionalAttributeValue(element, localName)
        ?? throw Invalid("The POS response contains invalid referential data.");

    private static string? OptionalAttributeValue(XElement element, string localName)
    {
        XAttribute[] attributes = element
            .Attributes()
            .Where(attribute => IsNamed(attribute, localName))
            .ToArray();
        if (attributes.Length > 1)
        {
            throw Invalid("The POS response contains ambiguous referential data.");
        }

        string? value = attributes.SingleOrDefault()?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static bool IsNamed(XElement element, string localName) =>
        element.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase);

    private static bool IsNamed(XAttribute attribute, string localName) =>
        attribute.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase);

    private static PosResponseException Invalid(string message) =>
        new("POS_INVALID_RESPONSE", message);
}
