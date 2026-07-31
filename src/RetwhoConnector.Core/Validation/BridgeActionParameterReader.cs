using System.Text.Json;
using RetwhoConnector.Core.Models;

namespace RetwhoConnector.Core.Validation;

public static class BridgeActionParameterReader
{
    private const string ObjectRequiredMessage = "Parameters must be a JSON object.";
    private const string UnsupportedPropertyMessage = "Parameters contain an unsupported property.";
    private const string PageMessage = "page must be an integer between 1 and 2147483647.";
    private const string PageSizeMessage = "pageSize must be an integer between 1 and 100.";
    private const string UpcRequiredMessage = "upc is required.";
    private const string UpcMessage = "upc must contain 1 to 32 digits.";
    private const string UpcModifierMessage = "upcModifier must contain exactly 3 digits.";
    private const string EmptyParametersMessage = "This action does not accept parameters.";

    public static PluPageQuery ReadPluPage(JsonElement parameters)
    {
        var page = 1;
        var pageSize = 100;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        EnsureObject(parameters);
        foreach (JsonProperty property in parameters.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw new ArgumentException(UnsupportedPropertyMessage);
            }

            switch (property.Name)
            {
                case "page":
                    page = ReadPositiveInteger(property.Value, PageMessage, int.MaxValue);
                    break;
                case "pageSize":
                    pageSize = ReadPositiveInteger(property.Value, PageSizeMessage, 100);
                    break;
                default:
                    throw new ArgumentException(UnsupportedPropertyMessage);
            }
        }

        return new PluPageQuery(page, pageSize);
    }

    public static PluLookupQuery ReadPluLookup(JsonElement parameters)
    {
        string? upc = null;
        var upcModifier = "000";
        var seen = new HashSet<string>(StringComparer.Ordinal);

        EnsureObject(parameters);
        foreach (JsonProperty property in parameters.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw new ArgumentException(UnsupportedPropertyMessage);
            }

            switch (property.Name)
            {
                case "upc":
                    upc = ReadString(property.Value, UpcMessage);
                    break;
                case "upcModifier":
                    upcModifier = ReadString(property.Value, UpcModifierMessage);
                    break;
                default:
                    throw new ArgumentException(UnsupportedPropertyMessage);
            }
        }

        if (upc is null)
        {
            throw new ArgumentException(UpcRequiredMessage);
        }

        if (!IsAsciiDigits(upc, 1, 32))
        {
            throw new ArgumentException(UpcMessage);
        }

        if (!IsAsciiDigits(upcModifier, 3, 3))
        {
            throw new ArgumentException(UpcModifierMessage);
        }

        return new PluLookupQuery(upc, upcModifier);
    }

    public static void ValidateEmpty(JsonElement parameters, string actionName)
    {
        _ = actionName;
        EnsureObject(parameters);
        foreach (JsonProperty _ in parameters.EnumerateObject())
        {
            throw new ArgumentException(EmptyParametersMessage);
        }
    }

    private static void EnsureObject(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(ObjectRequiredMessage);
        }
    }

    private static int ReadPositiveInteger(
        JsonElement value,
        string message,
        int maximum)
    {
        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out int parsed) ||
            parsed < 1 ||
            parsed > maximum)
        {
            throw new ArgumentException(message);
        }

        return parsed;
    }

    private static string ReadString(JsonElement value, string message)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException(message);
        }

        return value.GetString() ?? throw new ArgumentException(message);
    }

    private static bool IsAsciiDigits(string value, int minimumLength, int maximumLength)
    {
        if (value.Length < minimumLength || value.Length > maximumLength)
        {
            return false;
        }

        foreach (char character in value)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
