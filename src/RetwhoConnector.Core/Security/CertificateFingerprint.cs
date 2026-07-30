using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RetwhoConnector.Core.Security;

public static class CertificateFingerprint
{
    public static string FromCertificate(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return Convert.ToHexString(SHA256.HashData(certificate.RawData));
    }

    public static string Normalize(string fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        string normalized = new(
            fingerprint
                .Where(character =>
                    character != ':' &&
                    !char.IsWhiteSpace(character))
                .ToArray());
        if (normalized.Length != 64 ||
            normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "A SHA-256 fingerprint must contain 64 hexadecimal characters.",
                nameof(fingerprint));
        }

        return normalized.ToUpperInvariant();
    }

    public static bool Equals(string left, string right)
    {
        try
        {
            byte[] leftBytes = Convert.FromHexString(Normalize(left));
            byte[] rightBytes = Convert.FromHexString(Normalize(right));
            return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
