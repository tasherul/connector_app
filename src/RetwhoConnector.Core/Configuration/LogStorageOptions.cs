namespace RetwhoConnector.Core.Configuration;

public sealed record LogStorageOptions
{
    private static readonly string DefaultRoot = Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
        "RetwhoConnector");

    public string LogDirectory { get; init; } =
        Path.Combine(DefaultRoot, "Logs");

    public string DatabasePath { get; init; } =
        Path.Combine(DefaultRoot, "Data", "agent.db");

    public long MaximumFileBytes { get; init; } = 10 * 1024 * 1024;
    public int FileRetentionDays { get; init; } = 14;
    public int DatabaseRetentionDays { get; init; } = 30;
    public int MaximumDatabaseRows { get; init; } = 100_000;
    public int DatabaseBatchSize { get; init; } = 100;
    public TimeSpan DatabaseBatchInterval { get; init; } =
        TimeSpan.FromMilliseconds(500);

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(LogDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(DatabasePath);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumFileBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(FileRetentionDays, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            DatabaseRetentionDays,
            1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            MaximumDatabaseRows,
            1);
        ArgumentOutOfRangeException.ThrowIfLessThan(DatabaseBatchSize, 1);
        if (DatabaseBatchInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DatabaseBatchInterval));
        }
    }
}
