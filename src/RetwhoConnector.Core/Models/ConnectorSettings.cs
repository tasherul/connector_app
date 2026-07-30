namespace RetwhoConnector.Core.Models;

public sealed record ConnectorSettings
{
    public required string PosBaseUrl { get; init; }
    public required string PosUsername { get; init; }
    public required string PosPassword { get; init; }
    public required string LicenseKey { get; init; }
    public string? PosCookie { get; init; }
    public string? PinnedCertificateSha256 { get; init; }
    public bool AutoConnect { get; init; }

    public override string ToString() => nameof(ConnectorSettings);
}
