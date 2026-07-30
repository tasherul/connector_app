using System.Security.Cryptography;
using System.Text;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Exceptions;

namespace RetwhoConnector.Core.Security;

internal sealed class SecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("RetwhoConnector.Settings.v1");

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(plaintext);
            byte[] protectedBytes = ProtectedData.Protect(
                bytes,
                Entropy,
                DataProtectionScope.CurrentUser);
            CryptographicOperations.ZeroMemory(bytes);
            return Convert.ToBase64String(protectedBytes);
        }
        catch (Exception exception) when (
            exception is CryptographicException or PlatformNotSupportedException)
        {
            throw new SettingsException(
                "SETTINGS_ENCRYPTION_FAILED",
                "The connector could not encrypt its saved settings.",
                exception);
        }
    }

    public string Unprotect(string ciphertext)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        try
        {
            byte[] protectedBytes = Convert.FromBase64String(ciphertext);
            byte[] bytes = ProtectedData.Unprotect(
                protectedBytes,
                Entropy,
                DataProtectionScope.CurrentUser);
            string plaintext = Encoding.UTF8.GetString(bytes);
            CryptographicOperations.ZeroMemory(bytes);
            return plaintext;
        }
        catch (Exception exception) when (
            exception is FormatException or CryptographicException or
            PlatformNotSupportedException)
        {
            throw new SettingsException(
                "SETTINGS_DECRYPTION_FAILED",
                "The connector could not decrypt its saved settings.",
                exception);
        }
    }
}
