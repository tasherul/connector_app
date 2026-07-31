namespace RetwhoConnector.Core.Models;

public sealed record NamedReference
{
    public required string Id { get; init; }
    public required string Name { get; init; }
}

public sealed record DepartmentReference
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required bool IsFuel { get; init; }
    public string? ProductCode { get; init; }
}

public sealed record ProductCodeReference
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required bool IsFuel { get; init; }
}

public sealed record ReferenceDefinition
{
    public required string RecordType { get; init; }
    public string? Id { get; init; }
    public string? Name { get; init; }
    public IReadOnlyDictionary<string, string> Fields { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

public sealed record ReferentialIntegrityLimits
{
    public required int MaxRecords { get; init; }
    public required int MaxFeesPerItem { get; init; }
}

public sealed record ReferentialIntegrityResult
{
    public string Source { get; init; } = "NAXML";
    public string Command { get; init; } = "vrefinteg";
    public required string SiteId { get; init; }
    public required ReferentialIntegrityLimits Limits { get; init; }
    public IReadOnlyList<NamedReference> TaxRates { get; init; } = [];
    public IReadOnlyList<DepartmentReference> Departments { get; init; } = [];
    public IReadOnlyList<ProductCodeReference> ProductCodes { get; init; } = [];
    public IReadOnlyList<NamedReference> AgeValidations { get; init; } = [];
    public IReadOnlyList<ReferenceDefinition> Fees { get; init; } = [];
    public IReadOnlyList<ReferenceDefinition> BlueLaws { get; init; } = [];
    public required DateTimeOffset FetchedAtUtc { get; init; }
}
