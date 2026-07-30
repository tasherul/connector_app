using System.Text.RegularExpressions;

namespace RetwhoConnector.Core.Security;

public static partial class SensitiveDataRedactor
{
    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        string redacted = QuerySecretPattern().Replace(
            value,
            match => $"{match.Groups["name"].Value}=<redacted>");
        redacted = AuthorizationPattern().Replace(
            redacted,
            match => $"{match.Groups["name"].Value}: <redacted>");
        return redacted;
    }

    [GeneratedRegex(
        @"(?<name>passwd|password|user|username|cookie|license(?:Key)?|encrypted[A-Za-z0-9_]*)=[^&\s]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuerySecretPattern();

    [GeneratedRegex(
        @"(?<name>Authorization|Proxy-Authorization)\s*:\s*[^\r\n]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationPattern();
}
