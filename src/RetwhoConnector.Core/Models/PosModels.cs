using System.Net.Security;

namespace RetwhoConnector.Core.Models;

public sealed record PosSession
{
    public required string Cookie { get; init; }
    public string? SiteId { get; init; }
    public required DateTimeOffset ObtainedAtUtc { get; init; }

    public override string ToString() => nameof(PosSession);
}

public sealed record TimeZoneInfoDto
{
    public required string TimeZoneId { get; init; }
    public required int OffsetMinutes { get; init; }
    public required bool DstApplies { get; init; }
}

public sealed record VdatetimeResult
{
    public string Source { get; init; } = "NAXML";
    public string Command { get; init; } = "vdatetime";
    public required string SiteId { get; init; }
    public required string SystemDateTime { get; init; }
    public required string SystemTimeZoneId { get; init; }
    public required IReadOnlyList<TimeZoneInfoDto> TimeZones { get; init; }
    public required string RawXml { get; init; }
    public required DateTimeOffset FetchedAtUtc { get; init; }
}

public sealed record PosResponseMetadata
{
    public required int StatusCode { get; init; }
    public string? ReasonPhrase { get; init; }
    public string? ContentType { get; init; }
    public long? ContentLength { get; init; }
    public IReadOnlyList<string> ContentEncodings { get; init; } = [];
    public DateTimeOffset? Date { get; init; }
    public string? Server { get; init; }
    public string? Connection { get; init; }
    public TimeSpan? RetryAfter { get; init; }
    public bool HasSetCookieHeader { get; init; }
    public bool HasWwwAuthenticateHeader { get; init; }
}

public sealed record PosHttpResponse
{
    public required PosResponseMetadata Metadata { get; init; }
    public required string Body { get; init; }

    public override string ToString() =>
        $"{nameof(PosHttpResponse)}({Metadata.StatusCode})";
}

public sealed record PresentedCertificate
{
    public required string Subject { get; init; }
    public required string Issuer { get; init; }
    public required DateTimeOffset ValidFromUtc { get; init; }
    public required DateTimeOffset ValidToUtc { get; init; }
    public required string Sha256Fingerprint { get; init; }
    public required SslPolicyErrors PolicyErrors { get; init; }
    public bool IsSystemTrusted => PolicyErrors == SslPolicyErrors.None;
}
