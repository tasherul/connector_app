using System.Text.Json;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Exceptions;
using RetwhoConnector.Core.Models;
using RetwhoConnector.Core.Serialization;
using RetwhoConnector.Core.Validation;

namespace RetwhoConnector.Core.Services;

public sealed class SecureSettingsService : ISecureSettingsService
{
    private readonly ISettingsFileStore _fileStore;
    private readonly ISecretProtector _protector;
    private readonly string _settingsPath;

    public SecureSettingsService()
        : this(
            new SettingsFileStore(),
            new Security.SecretProtector(),
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "RetwhoConnector",
                "settings.json"))
    {
    }

    internal SecureSettingsService(
        ISettingsFileStore fileStore,
        ISecretProtector protector,
        string settingsPath)
    {
        _fileStore = fileStore;
        _protector = protector;
        _settingsPath = settingsPath;
    }

    public async Task<ConnectorSettings?> LoadAsync(
        CancellationToken cancellationToken)
    {
        string? json = await _fileStore.ReadAsync(
            _settingsPath,
            cancellationToken).ConfigureAwait(false);
        if (json is null)
        {
            return null;
        }

        try
        {
            StoredSettings stored =
                JsonSerializer.Deserialize<StoredSettings>(
                    json,
                    ConnectorJson.Options)
                ?? throw new JsonException("The settings document is empty.");
            if (stored.SchemaVersion != 1)
            {
                throw new JsonException("Unsupported settings schema.");
            }

            return ConnectorSettingsValidator.Validate(new ConnectorSettings
            {
                PosBaseUrl = stored.PosBaseUrl,
                PosUsername = _protector.Unprotect(stored.EncryptedPosUsername),
                PosPassword = _protector.Unprotect(stored.EncryptedPosPassword),
                LicenseKey = _protector.Unprotect(stored.EncryptedLicenseKey),
                PosCookie = UnprotectOptional(stored.EncryptedPosCookie),
                PinnedCertificateSha256 =
                    UnprotectOptional(stored.EncryptedCertificateSha256),
                AutoConnect = stored.AutoConnect,
            });
        }
        catch (SettingsException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException or ArgumentException)
        {
            throw new SettingsException(
                "SETTINGS_CORRUPT",
                "Saved connector settings are corrupt. Back up or clear the file before continuing.",
                exception);
        }
    }

    public async Task SaveAsync(
        ConnectorSettings settings,
        CancellationToken cancellationToken)
    {
        ConnectorSettings validated = ConnectorSettingsValidator.Validate(settings);
        ConnectorSettings? existing = await LoadAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            bool hostChanged = !string.Equals(
                existing.PosBaseUrl,
                validated.PosBaseUrl,
                StringComparison.OrdinalIgnoreCase);
            bool credentialsChanged =
                !string.Equals(
                    existing.PosUsername,
                    validated.PosUsername,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    existing.PosPassword,
                    validated.PosPassword,
                    StringComparison.Ordinal);

            validated = validated with
            {
                PosCookie = hostChanged || credentialsChanged
                    ? null
                    : validated.PosCookie,
                PinnedCertificateSha256 = hostChanged
                    ? null
                    : validated.PinnedCertificateSha256,
            };
        }

        var stored = new StoredSettings
        {
            SchemaVersion = 1,
            PosBaseUrl = validated.PosBaseUrl,
            EncryptedPosUsername = _protector.Protect(validated.PosUsername),
            EncryptedPosPassword = _protector.Protect(validated.PosPassword),
            EncryptedLicenseKey = _protector.Protect(validated.LicenseKey),
            EncryptedPosCookie = ProtectOptional(validated.PosCookie),
            EncryptedCertificateSha256 =
                ProtectOptional(validated.PinnedCertificateSha256),
            AutoConnect = validated.AutoConnect,
        };
        string json = JsonSerializer.Serialize(
            stored,
            ConnectorJson.Options);

        try
        {
            await _fileStore.WriteAtomicAsync(
                _settingsPath,
                json,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new SettingsException(
                "SETTINGS_SAVE_FAILED",
                "The connector could not save its settings.",
                exception);
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken) =>
        _fileStore.DeleteAsync(_settingsPath, cancellationToken);

    private string? ProtectOptional(string? value) =>
        string.IsNullOrEmpty(value) ? null : _protector.Protect(value);

    private string? UnprotectOptional(string? value) =>
        string.IsNullOrEmpty(value) ? null : _protector.Unprotect(value);

    private sealed record StoredSettings
    {
        public required int SchemaVersion { get; init; }
        public required string PosBaseUrl { get; init; }
        public required string EncryptedPosUsername { get; init; }
        public required string EncryptedPosPassword { get; init; }
        public required string EncryptedLicenseKey { get; init; }
        public string? EncryptedPosCookie { get; init; }
        public string? EncryptedCertificateSha256 { get; init; }
        public bool AutoConnect { get; init; }
    }
}

internal sealed class SettingsFileStore : ISettingsFileStore
{
    public async Task<string?> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        return await File.ReadAllTextAsync(path, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task WriteAtomicAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
        {
            throw new IOException("Settings path has no directory.");
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = path + ".tmp";
        string backupPath = path + ".bak";
        try
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(content);
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                File.Replace(temporaryPath, path, backupPath);
                File.Delete(backupPath);
            }
            else
            {
                File.Move(temporaryPath, path);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }
}
