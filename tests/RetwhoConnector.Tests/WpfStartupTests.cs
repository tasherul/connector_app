using System.Xml.Linq;

namespace RetwhoConnector.Tests;

public sealed class WpfStartupTests
{
    [Fact]
    public void Application_UsesExplicitShutdownDuringAsyncStartup()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "App.xaml");
        XDocument document = XDocument.Load(path);

        Assert.Equal(
            "OnExplicitShutdown",
            document.Root?.Attribute("ShutdownMode")?.Value);
    }

    [Fact]
    public void Application_DoesNotReplaceExplicitShutdownModeAtRuntime()
    {
        string source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "App.xaml.cs.txt"));

        Assert.DoesNotContain(
            "ShutdownMode.OnMainWindowClose",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "trayIcon.Initialize(window)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Application_RemovesFrameworkLoggersFromPosHttpClient()
    {
        string source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "App.xaml.cs.txt"));

        Assert.Contains(
            "AddHttpClient<PosHttpClient>()",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            ".RemoveAllLoggers()",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_HidesOnCloseAndMinimizeUntilExplicitExit()
    {
        string source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "MainWindow.xaml.cs.txt"));

        Assert.Contains("e.Cancel = true", source, StringComparison.Ordinal);
        Assert.Contains("WindowState.Minimized", source, StringComparison.Ordinal);
        Assert.Contains("Hide()", source, StringComparison.Ordinal);
        Assert.Contains("IsExitRequested", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_ExposesApprovedDashboardAndActivityFeed()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "MainWindow.xaml");
        XDocument document = XDocument.Load(path);
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        Assert.Equal(
            "Hybrid Edge Connector Agent",
            document.Root?.Attribute("Title")?.Value);
        string[] cardNames = document
            .Descendants(presentation + "Border")
            .Select(element =>
                element.Attribute("AutomationProperties.Name")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();
        Assert.Contains("Configuration status", cardNames);
        Assert.Contains("Cloud server status", cardNames);
        Assert.Contains("Agent health status", cardNames);
        Assert.Contains("Logging health status", cardNames);

        XElement activityList = Assert.Single(
            document.Descendants(presentation + "ListBox"),
            element =>
                element.Attribute("ItemsSource")?.Value.Contains(
                    "ActivityEntries",
                    StringComparison.Ordinal) == true);
        Assert.Equal(
            "True",
            activityList.Attribute(
                "VirtualizingStackPanel.IsVirtualizing")?.Value);
    }

    [Fact]
    public void ConfigurationWindow_UsesMaskedSecretsAndApprovedActions()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "ConfigurationWindow.xaml");
        XDocument document = XDocument.Load(path);
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        string[] passwordNames = document
            .Descendants(presentation + "PasswordBox")
            .Select(element => element.Attribute(x + "Name")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();
        Assert.Contains("LicenseKeyBox", passwordNames);
        Assert.Contains("PosPasswordBox", passwordNames);

        string[] buttons = document
            .Descendants(presentation + "Button")
            .Select(element => element.Attribute("Content")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();
        Assert.Contains("Save & Test Connection", buttons);
        Assert.Contains("Cancel", buttons);
    }
}
