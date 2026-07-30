using System.Text.RegularExpressions;
using RetwhoConnector.Core.Models;

namespace RetwhoConnector.Core.Validation;

public static partial class ConnectorSettingsValidator
{
    public static ConnectorSettings Validate(ConnectorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!Uri.TryCreate(settings.PosBaseUrl, UriKind.Absolute, out Uri? uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            (uri.AbsolutePath != "/" && !string.IsNullOrEmpty(uri.AbsolutePath)))
        {
            throw new ArgumentException(
                "The POS URL must be an HTTPS origin without a path, query, fragment, or user information.",
                nameof(settings));
        }

        if (string.IsNullOrWhiteSpace(settings.PosUsername) ||
            string.IsNullOrWhiteSpace(settings.PosPassword))
        {
            throw new ArgumentException(
                "POS username and password are required.",
                nameof(settings));
        }

        if (!LicensePattern().IsMatch(settings.LicenseKey))
        {
            throw new ArgumentException(
                "The license key has an invalid format.",
                nameof(settings));
        }

        var builder = new UriBuilder(uri)
        {
            Path = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty,
        };

        string normalized = builder.Uri.GetLeftPart(UriPartial.Authority);
        return settings with { PosBaseUrl = normalized };
    }

    [GeneratedRegex("^[A-Za-z0-9._:~-]{1,255}$", RegexOptions.CultureInvariant)]
    private static partial Regex LicensePattern();
}
