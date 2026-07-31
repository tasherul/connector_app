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
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        Assert.Equal(
            "Hybrid Edge Connector Agent",
            document.Root?.Attribute("Title")?.Value);
        XElement rail = Assert.Single(
            document.Descendants(presentation + "Border"),
            element =>
                element.Attribute("AutomationProperties.Name")?.Value ==
                "Connector status rail container");
        Assert.Contains(
            rail.Descendants(presentation + "UniformGrid"),
            element =>
                element.Attribute("Columns")?.Value.Contains(
                    "WidthToStatusColumnCount",
                    StringComparison.Ordinal) == true);

        AssertStatusSegment(
            rail,
            presentation,
            "Configuration status indicator",
            "ConfigurationIndicator");
        AssertStatusSegment(
            rail,
            presentation,
            "Cloud server status indicator",
            "ServerIndicator");
        AssertStatusSegment(
            rail,
            presentation,
            "Agent status indicator",
            "AgentIndicator");
        AssertStatusSegment(
            rail,
            presentation,
            "Logging status indicator",
            "LoggingIndicator");

        XElement statusTemplate = Assert.Single(
            document.Descendants(presentation + "DataTemplate"),
            element => element.Attribute(x + "Key")?.Value ==
                "StatusRailSegmentTemplate");
        Assert.Contains(statusTemplate.Descendants(presentation + "Path"), _ => true);
        Assert.Contains(statusTemplate.Descendants(presentation + "Ellipse"),
            element =>
                element.Attribute("Fill")?.Value.Contains(
                    "DashboardSignalBrush",
                    StringComparison.Ordinal) == true);
        Assert.Contains(statusTemplate.Descendants(presentation + "TextBlock"),
            element => element.Attribute("Text")?.Value.Contains(
                "Binding Title",
                StringComparison.Ordinal) == true);
        Assert.Contains(statusTemplate.Descendants(presentation + "TextBlock"),
            element => element.Attribute("Text")?.Value == "{Binding Status}");
        Assert.Contains(statusTemplate.Descendants(presentation + "TextBlock"),
            element =>
                element.Attribute("Text")?.Value == "{Binding Description}" &&
                element.Attribute("ToolTip")?.Value == "{Binding Description}");

        string xaml = File.ReadAllText(path);
        Assert.DoesNotContain("🔧", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("☁", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("⛨", xaml, StringComparison.Ordinal);

        AssertButtonStyle(
            document,
            presentation,
            "Open connection settings",
            "SettingsButtonStyle");
        AssertButtonStyle(
            document,
            presentation,
            "{Binding ConnectionActionText}",
            "ConnectionButtonStyle");
        AssertButtonStyle(
            document,
            presentation,
            "Open logs folder",
            "LogsButtonStyle");
        AssertButtonStyle(
            document,
            presentation,
            "Exit application",
            "DangerButtonStyle");

        XElement connectionButton = Assert.Single(
            document.Descendants(presentation + "Button"),
            element => element.Attribute("Content")?.Value == "{Binding ConnectionActionText}");
        Assert.Equal(
            "{Binding ConnectionActionText}",
            connectionButton.Attribute("AutomationProperties.Name")?.Value);

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
        Assert.Equal(
            "Recycling",
            activityList.Attribute(
                "VirtualizingStackPanel.VirtualizationMode")?.Value);
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

        AssertExplicitForeground(
            document,
            presentation,
            "Connection & License Configuration");
        AssertExplicitForeground(
            document,
            presentation,
            "Settings are tested before encrypted storage is changed.");
        Assert.Contains(
            document.Descendants(presentation + "TextBlock"),
            element =>
                element.Attribute("Text")?.Value == "{Binding ValidationMessage}" &&
                element.Attribute("Foreground")?.Value ==
                "{DynamicResource ErrorTextBrush}");

        AssertButtonStyle(
            document,
            presentation,
            "Clear Saved Settings",
            "DangerButtonStyle");
        XElement clearButton = Assert.Single(
            document.Descendants(presentation + "Button"),
            element => element.Attribute("Content")?.Value == "Clear Saved Settings");
        Assert.Equal("{x:Null}", clearButton.Attribute("Tag")?.Value);
        AssertButtonStyle(
            document,
            presentation,
            "Cancel",
            "DialogSecondaryButtonStyle");
        AssertButtonStyle(
            document,
            presentation,
            "Save & Test Connection",
            "DialogPrimaryButtonStyle");
    }

    [Fact]
    public void MainWindow_UsesAdaptiveTitleActionsAndSeparators()
    {
        XDocument document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "MainWindow.xaml"));
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        XElement statusTemplate = Assert.Single(
            document.Descendants(presentation + "DataTemplate"),
            element => element.Attribute(x + "Key")?.Value ==
                "StatusRailSegmentTemplate");
        Assert.Contains(statusTemplate.Descendants(presentation + "TextBlock"),
            element =>
                element.Attribute("Text")?.Value ==
                "{Binding Title, Converter={StaticResource UppercaseText}}");

        XElement commandBar = Assert.Single(
            document.Descendants(presentation + "WrapPanel"),
            element => element.Attribute("AutomationProperties.Name")?.Value ==
                "Connector command actions");
        Assert.Equal("1", commandBar.Attribute("Grid.Row")?.Value);
        Assert.Equal("Stretch", commandBar.Attribute("HorizontalAlignment")?.Value);

        AssertRailSeparatorStyle(
            document,
            presentation,
            x,
            "ConfigurationRailSegmentStyle",
            "0,0,1,0",
            "0,0,1,1");
        AssertRailSeparatorStyle(
            document,
            presentation,
            x,
            "ServerRailSegmentStyle",
            "0,0,1,0",
            "0,0,0,1");
        AssertRailSeparatorStyle(
            document,
            presentation,
            x,
            "AgentRailSegmentStyle",
            "0,0,1,0",
            "0,0,1,0");
        AssertRailSeparatorStyle(
            document,
            presentation,
            x,
            "LogsRailSegmentStyle",
            "0",
            null);
    }

    private static void AssertStatusSegment(
        XElement rail,
        XNamespace presentation,
        string automationName,
        string bindingName)
    {
        XElement segment = Assert.Single(
            rail.Descendants(presentation + "ContentControl"),
            element =>
                element.Attribute("AutomationProperties.Name")?.Value ==
                automationName);

        Assert.Equal(
            $"{{Binding {bindingName}}}",
            segment.Attribute("Content")?.Value);
        Assert.Contains(
            "StatusRailSegmentTemplate",
            segment.Attribute("ContentTemplate")?.Value,
            StringComparison.Ordinal);
    }

    private static void AssertButtonStyle(
        XDocument document,
        XNamespace presentation,
        string contentOrAutomationName,
        string styleKey)
    {
        Assert.Contains(
            document.Descendants(presentation + "Button"),
            element =>
                (element.Attribute("Content")?.Value == contentOrAutomationName ||
                 element.Attribute("AutomationProperties.Name")?.Value ==
                 contentOrAutomationName) &&
                element.Attribute("Style")?.Value.Contains(
                    styleKey,
                    StringComparison.Ordinal) == true);
    }

    private static void AssertExplicitForeground(
        XDocument document,
        XNamespace presentation,
        string text)
    {
        Assert.Contains(
            document.Descendants(presentation + "TextBlock"),
            element =>
                element.Attribute("Text")?.Value == text &&
                element.Attribute("Foreground")?.Value.Contains(
                    "TextBrush",
                    StringComparison.Ordinal) == true);
    }

    private static void AssertRailSeparatorStyle(
        XDocument document,
        XNamespace presentation,
        XNamespace x,
        string key,
        string wideThickness,
        string? narrowThickness)
    {
        XElement style = Assert.Single(
            document.Descendants(presentation + "Style"),
            element => element.Attribute(x + "Key")?.Value == key);
        Assert.Contains(style.Elements(presentation + "Setter"),
            element =>
                element.Attribute("Property")?.Value == "BorderThickness" &&
                element.Attribute("Value")?.Value == wideThickness);

        if (narrowThickness is null)
        {
            Assert.Empty(style.Descendants(presentation + "DataTrigger"));
            return;
        }

        XElement trigger = Assert.Single(
            style.Descendants(presentation + "DataTrigger"),
            element => element.Attribute("Value")?.Value == "2");
        Assert.Contains("UniformGrid", trigger.Attribute("Binding")?.Value);
        Assert.Contains(trigger.Elements(presentation + "Setter"),
            element =>
                element.Attribute("Property")?.Value == "BorderThickness" &&
                element.Attribute("Value")?.Value == narrowThickness);
    }
}
