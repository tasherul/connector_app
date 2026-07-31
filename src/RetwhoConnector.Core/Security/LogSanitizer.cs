using System.Text.RegularExpressions;
using RetwhoConnector.Core.Abstractions;

namespace RetwhoConnector.Core.Security;

public sealed partial class LogSanitizer : ILogSanitizer
{
    private const string Redacted = "<redacted>";

    public string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        string result = CredentialUriPattern().Replace(
            value,
            match => $"{match.Groups["scheme"].Value}{Redacted}@");
        result = AuthorizationHeaderPattern().Replace(
            result,
            match => $"{match.Groups["name"].Value}: {Redacted}");
        result = XmlSecretPattern().Replace(
            result,
            match =>
                $"{match.Groups["open"].Value}{Redacted}" +
                match.Groups["close"].Value);
        result = JsonSecretPattern().Replace(
            result,
            match =>
                $"{match.Groups["prefix"].Value}{Redacted}" +
                match.Groups["suffix"].Value);
        result = KeyValueSecretPattern().Replace(
            result,
            match => $"{match.Groups["prefix"].Value}{Redacted}");
        result = LabeledSecretPattern().Replace(
            result,
            match => $"{match.Groups["prefix"].Value}{Redacted}");
        return result;
    }

    [GeneratedRegex(
        @"(?<scheme>\bhttps?://)[^/\s@]+@",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CredentialUriPattern();

    [GeneratedRegex(
        @"(?<name>Authorization|Proxy-Authorization)\s*:\s*[^\r\n]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationHeaderPattern();

    [GeneratedRegex(
        @"(?<open><(?<tag>(?:[A-Za-z_][\w.-]*:)?(?:cookie|password|passwd|username|user|licenseKey|license|authorization|encrypted[A-Za-z0-9_]*))\b[^>]*>)[\s\S]*?(?<close></\k<tag>\s*>)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex XmlSecretPattern();

    [GeneratedRegex(
        "(?<prefix>\\\"(?:password|passwd|username|user|cookie|licenseKey|license|authorization|encrypted[^\\\"]*)\\\"\\s*:\\s*\\\")" +
        "(?:\\\\.|[^\\\"\\\\])*(?<suffix>\\\")",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JsonSecretPattern();

    [GeneratedRegex(
        @"(?<prefix>\b(?:passwd|password|user|username|cookie|license(?:Key)?|encrypted[A-Za-z0-9_]*)\s*=\s*)(?:[""'][^""']*[""']|[^&;\s]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KeyValueSecretPattern();

    [GeneratedRegex(
        @"(?<prefix>\b(?:license\s+key|license|username|user|password|passwd|cookie|encrypted[A-Za-z0-9_]*)\s*:\s*)[^;\r\n]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LabeledSecretPattern();
}
