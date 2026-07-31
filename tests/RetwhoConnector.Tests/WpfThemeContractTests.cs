using System.Xml.Linq;

namespace RetwhoConnector.Tests;

public sealed class WpfThemeContractTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ThemeBrushes_UseVisibleValuesAndReadableSemanticContrasts()
    {
        XDocument colors = LoadFixture("Colors.xaml");
        IReadOnlyDictionary<string, string> values = BrushValues(colors);

        Assert.All(values, pair =>
            Assert.False(
                string.IsNullOrWhiteSpace(pair.Value) ||
                pair.Value.Equals("Transparent", StringComparison.OrdinalIgnoreCase) ||
                pair.Value.StartsWith("#00", StringComparison.OrdinalIgnoreCase),
                $"{pair.Key} must be visible."));
        Assert.NotEqual(values["BackgroundBrush"], values["PrimaryTextBrush"]);
        Assert.NotEqual(values["BackgroundBrush"], values["SecondaryTextBrush"]);
        Assert.NotEqual(values["DisabledTextBrush"], values["DisabledBackgroundBrush"]);
        Assert.NotEqual(values["ValidationTextBrush"], values["BackgroundBrush"]);
        Assert.NotEqual(values["ValidationTextBrush"], values["DialogBackgroundBrush"]);
    }

    [Fact]
    public void InputControls_DeclareForegroundDistinctFromInputBackground()
    {
        XDocument colors = LoadFixture("Colors.xaml");
        XDocument controls = LoadFixture("Controls.xaml");
        IReadOnlyDictionary<string, string> values = BrushValues(colors);

        foreach (string targetType in new[] { "TextBox", "PasswordBox" })
        {
            XElement style = Assert.Single(
                controls.Descendants(Presentation + "Style"),
                element => element.Attribute("TargetType")?.Value == targetType);
            string background = ResourceKey(SetterValue(style, "Background"));
            string foreground = ResourceKey(SetterValue(style, "Foreground"));

            Assert.NotEqual(values[background], values[foreground]);
        }
    }

    [Fact]
    public void IconDictionary_ProvidesEveryApprovedVectorGeometry()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Icons.xaml");
        Assert.True(File.Exists(path), "The vector icon resource dictionary must be linked as a test fixture.");

        XDocument icons = XDocument.Load(path);
        string[] keys = icons.Descendants()
            .Select(element => element.Attribute(X + "Key")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();

        foreach (string key in ApprovedIconKeys)
        {
            Assert.Contains(key, keys);
        }
    }

    [Fact]
    public void Controls_ProvideApprovedModernButtonStylesAndVisualStates()
    {
        XDocument controls = LoadFixture("Controls.xaml");
        string[] styleKeys = controls.Descendants(Presentation + "Style")
            .Select(element => element.Attribute(X + "Key")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();

        foreach (string key in ApprovedButtonStyleKeys)
        {
            Assert.Contains(key, styleKeys);
        }

        XElement baseStyle = Assert.Single(
            controls.Descendants(Presentation + "Style"),
            element => element.Attribute(X + "Key")?.Value == "ModernButtonBaseStyle");
        XElement template = Assert.Single(baseStyle.Descendants(Presentation + "ControlTemplate"));
        XElement triggers = Assert.Single(template.Elements(Presentation + "ControlTemplate.Triggers"));

        Assert.Contains(triggers.Elements(Presentation + "Trigger"), trigger =>
            trigger.Attribute("Property")?.Value == "IsMouseOver" &&
            trigger.Attribute("Value")?.Value == "True");
        Assert.Contains(triggers.Elements(Presentation + "Trigger"), trigger =>
            trigger.Attribute("Property")?.Value == "IsPressed" &&
            trigger.Attribute("Value")?.Value == "True");
        Assert.Contains(triggers.Elements(Presentation + "Trigger"), trigger =>
            trigger.Attribute("Property")?.Value == "IsEnabled" &&
            trigger.Attribute("Value")?.Value == "False");
        Assert.Contains(triggers.Elements(Presentation + "Trigger"), trigger =>
            trigger.Attribute("Property")?.Value == "IsKeyboardFocused" &&
            trigger.Attribute("Value")?.Value == "True");
    }

    [Fact]
    public void ConnectionButton_SwitchesToDisconnectIconAndPalette()
    {
        XDocument controls = LoadFixture("Controls.xaml");
        XElement style = Assert.Single(
            controls.Descendants(Presentation + "Style"),
            element => element.Attribute(X + "Key")?.Value == "ConnectionButtonStyle");
        XElement trigger = Assert.Single(
            style.Descendants(Presentation + "DataTrigger"),
            element => element.Attribute("Binding")?.Value.Contains(
                "ConnectionActionText",
                StringComparison.Ordinal) == true &&
                element.Attribute("Value")?.Value == "Disconnect");

        Assert.Contains(trigger.Elements(Presentation + "Setter"), setter =>
            setter.Attribute("Property")?.Value == "Tag" &&
            setter.Attribute("Value")?.Value.Contains("DisconnectIconGeometry", StringComparison.Ordinal) == true);
        Assert.Contains(trigger.Elements(Presentation + "Setter"), setter =>
            setter.Attribute("Property")?.Value == "Background");
        Assert.Contains(trigger.Elements(Presentation + "Setter"), setter =>
            setter.Attribute("Property")?.Value == "Foreground");
    }

    [Fact]
    public void Application_MergesIconsBeforeControls()
    {
        XDocument app = LoadFixture("App.xaml");
        string[] sources = app.Descendants(Presentation + "ResourceDictionary")
            .Select(element => element.Attribute("Source")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();

        Assert.True(Array.IndexOf(sources, "/Styles/Icons.xaml") >= 0);
        Assert.True(
            Array.IndexOf(sources, "/Styles/Icons.xaml") <
            Array.IndexOf(sources, "/Styles/Controls.xaml"));
    }

    private static readonly string[] ApprovedIconKeys =
    [
        "ConfigurationIconGeometry",
        "CloudIconGeometry",
        "AgentIconGeometry",
        "LogsIconGeometry",
        "SettingsIconGeometry",
        "FolderIconGeometry",
        "ConnectIconGeometry",
        "DisconnectIconGeometry",
        "ExitIconGeometry",
    ];

    private static readonly string[] ApprovedButtonStyleKeys =
    [
        "SettingsButtonStyle",
        "ConnectionButtonStyle",
        "LogsButtonStyle",
        "DangerButtonStyle",
        "DialogPrimaryButtonStyle",
        "DialogSecondaryButtonStyle",
    ];

    private static XDocument LoadFixture(string name) =>
        XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static IReadOnlyDictionary<string, string> BrushValues(XDocument document) =>
        document.Descendants(Presentation + "SolidColorBrush")
            .ToDictionary(
                element => element.Attribute(X + "Key")!.Value,
                element => element.Attribute("Color")!.Value,
                StringComparer.Ordinal);

    private static string SetterValue(XElement style, string property) =>
        Assert.Single(
                style.Elements(Presentation + "Setter"),
                setter => setter.Attribute("Property")?.Value == property)
            .Attribute("Value")!
            .Value;

    private static string ResourceKey(string markup)
    {
        int keyStart = markup.IndexOf(" ", StringComparison.Ordinal) + 1;
        return markup[keyStart..].TrimEnd('}');
    }
}
