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

    [Fact]
    public void Readme_DocumentsSafeSapphireSessionRecoveryDiagnostics()
    {
        string readme = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "README.md"));

        Assert.Contains(
            "CGIPortal.LoginRequired",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "one `validate` login and one retry of the original POS data action",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "`faultCode`, `faultString`, and `message`",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "does not log raw request or response payloads",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "reopen **Settings**",
            readme,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_DocumentsPluBridgeActionParametersAndPagination()
    {
        string readme = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "README.md"));

        Assert.Contains("`get_plu_page`", readme, StringComparison.Ordinal);
        Assert.Contains("`get_plu`", readme, StringComparison.Ordinal);
        Assert.Contains(
            "`get_referential_integrity`",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "`pageSize` defaults to `100`",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "`upcModifier` defaults to `000`",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "`prodCodes,departments,ageValidations,taxRates,blueLaws,fees`",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "exactly one POS page",
            readme,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_DocumentsPluResultSafetyAndSingleSessionRefresh()
    {
        string readme = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "README.md"));

        Assert.Contains(
            "`ok: true` and `found: false`",
            readme,
            StringComparison.Ordinal);
        Assert.Contains("omit `rawXml`", readme, StringComparison.Ordinal);
        Assert.Contains(
            "one `validate` login and one retry",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "inventory payloads",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "credential-bearing requests",
            readme,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_DocumentsModernDashboardAccessibilityAndWindowsAcceptance()
    {
        string readme = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "README.md"));

        Assert.Contains(
            "compact status rail",
            readme,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "four columns",
            readme,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "two-by-two",
            readme,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "keyboard focus",
            readme,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "high-contrast",
            readme,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "screenshots",
            readme,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Windows-only STA smoke tests",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "unverified on non-Windows",
            readme,
            StringComparison.OrdinalIgnoreCase);
    }
}
