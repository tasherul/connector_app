namespace RetwhoConnector.Tests;

public sealed class DocumentationTests
{
    [Fact]
    public void Readme_DocumentsDashboardTrayAndAllLogStores()
    {
        string readme = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "README.md"));

        Assert.StartsWith(
            "# Hybrid Edge Connector Agent",
            readme,
            StringComparison.Ordinal);
        Assert.Contains("## Status dashboard", readme, StringComparison.Ordinal);
        Assert.Contains(
            "## Notification-area behavior",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            @"%LocalAppData%\RetwhoConnector\Data\agent.db",
            readme,
            StringComparison.Ordinal);
        Assert.Contains("newest 1,000", readme, StringComparison.Ordinal);
        Assert.Contains("14 days", readme, StringComparison.Ordinal);
        Assert.Contains("100,000", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_UsesTheTransactionalConfigurationWorkflow()
    {
        string readme = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "README.md"));

        Assert.Contains(
            "**Save & Test Connection**",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "old saved settings remain unchanged",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "first run",
            readme,
            StringComparison.OrdinalIgnoreCase);
    }
}
