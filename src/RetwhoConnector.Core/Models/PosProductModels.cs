namespace RetwhoConnector.Core.Models;

public sealed record PluPageQuery(int Page, int PageSize);

public sealed record PluLookupQuery(string Upc, string UpcModifier);

public sealed record IndexedCode
{
    public required int Index { get; init; }
    public required string Code { get; init; }
}

public sealed record PluProduct
{
    public required string Upc { get; init; }
    public required string UpcModifier { get; init; }
    public required string Description { get; init; }
    public required string DepartmentId { get; init; }
    public IReadOnlyList<string> FeeIds { get; init; } = [];
    public string? ProductCode { get; init; }
    public decimal? Price { get; init; }
    public IReadOnlyList<string> FlagIds { get; init; } = [];
    public IReadOnlyList<string> TaxRateIds { get; init; } = [];
    public IReadOnlyList<string> IdCheckIds { get; init; } = [];
    public decimal? SellUnit { get; init; }
    public decimal? TaxableRebateAmount { get; init; }
    public IReadOnlyList<IndexedCode> GroupCodes { get; init; } = [];
    public decimal? MaxQuantityPerTransaction { get; init; }
}

public sealed record PluPageResult
{
    public string Source { get; init; } = "NAXML";
    public string Command { get; init; } = "vPLUs";
    public required int Page { get; init; }
    public required int TotalPages { get; init; }
    public required int RequestedPageSize { get; init; }
    public required int ItemCount { get; init; }
    public IReadOnlyList<PluProduct> Products { get; init; } = [];
    public required DateTimeOffset FetchedAtUtc { get; init; }
}

public sealed record PluLookupResult
{
    public string Source { get; init; } = "NAXML";
    public string Command { get; init; } = "vPLU";
    public required string RequestedUpc { get; init; }
    public required string RequestedUpcModifier { get; init; }
    public required bool Found { get; init; }
    public PluProduct? Product { get; init; }
    public required DateTimeOffset FetchedAtUtc { get; init; }
}
