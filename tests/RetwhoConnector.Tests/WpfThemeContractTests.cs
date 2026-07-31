using System.Xml.Linq;

namespace RetwhoConnector.Tests;

public sealed class WpfThemeContractTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X =
        "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace PresentationOptions =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation/options";

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
    public void ThemeTextBrushes_MeetWcagContrastAgainstTheirSurfaces()
    {
        IReadOnlyDictionary<string, string> values = BrushValues(LoadFixture("Colors.xaml"));

        Assert.Contains("ErrorTextBrush", values.Keys);
        AssertContrastAtLeast(values["PrimaryTextBrush"], values["BackgroundBrush"], 4.5);
        AssertContrastAtLeast(values["SecondaryTextBrush"], values["BackgroundBrush"], 4.5);
        AssertContrastAtLeast(values["InputForegroundBrush"], values["InputBackgroundBrush"], 4.5);
        AssertContrastAtLeast(values["DisabledTextBrush"], values["DisabledBackgroundBrush"], 4.5);
        AssertContrastAtLeast(values["ErrorTextBrush"], values["SurfaceRaisedBrush"], 4.5);
        AssertContrastAtLeast(values["ValidationTextBrush"], values["DialogBackgroundBrush"], 4.5);
    }

    [Fact]
    public void ConfigurationValidation_UsesTheContrastSafeErrorTextBrush()
    {
        XDocument configuration = LoadFixture("ConfigurationWindow.xaml");
        XElement validation = Assert.Single(
            configuration.Descendants(Presentation + "TextBlock"),
            element => element.Attribute("Text")?.Value.Contains(
                "ValidationMessage",
                StringComparison.Ordinal) == true);

        Assert.Equal(
            "ErrorTextBrush",
            ResourceKey(validation.Attribute("Foreground")!.Value));
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
        foreach (string key in ApprovedIconKeys)
        {
            XElement geometry = Assert.Single(
                icons.Descendants(),
                element => element.Attribute(X + "Key")?.Value == key);
            Assert.Equal("PathGeometry", geometry.Name.LocalName);
            Assert.Equal("True", geometry.Attribute(PresentationOptions + "Freeze")?.Value);
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
        AssertTemplateStateSetter(triggers, "IsMouseOver", "ButtonHoverBackgroundBrush");
        AssertTemplateStateSetter(triggers, "IsPressed", "ButtonPressedBackgroundBrush");

        XElement icon = Assert.Single(
            template.Descendants(Presentation + "Path"),
            element => element.Attribute(X + "Name")?.Value == "ButtonIcon");
        XElement content = Assert.Single(
            template.Descendants(Presentation + "ContentPresenter"),
            element => element.Attribute(X + "Name")?.Value == "ButtonContent");
        Assert.Equal("{TemplateBinding Tag}", icon.Attribute("Data")?.Value);

        XElement noIcon = Assert.Single(
            triggers.Elements(Presentation + "Trigger"),
            trigger => trigger.Attribute("Property")?.Value == "Tag" &&
                trigger.Attribute("Value")?.Value == "{x:Null}");
        Assert.Contains(noIcon.Elements(Presentation + "Setter"), setter =>
            setter.Attribute("TargetName")?.Value == "ButtonIcon" &&
            setter.Attribute("Property")?.Value == "Visibility" &&
            setter.Attribute("Value")?.Value == "Collapsed");
        Assert.Contains(noIcon.Elements(Presentation + "Setter"), setter =>
            setter.Attribute("TargetName")?.Value == "ButtonContent" &&
            setter.Attribute("Property")?.Value == "Margin" &&
            setter.Attribute("Value")?.Value == "0");
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

        AssertStyleStateSetter(style, "IsMouseOver", "SuccessButtonHoverBrush");
        AssertStyleStateSetter(style, "IsPressed", "SuccessButtonPressedBrush");
        AssertDisconnectStateSetter(style, "IsMouseOver", "DangerButtonHoverBrush");
        AssertDisconnectStateSetter(style, "IsPressed", "DangerButtonPressedBrush");
    }

    [Fact]
    public void DangerButton_UsesDangerPaletteForHoverAndPressedStates()
    {
        XDocument controls = LoadFixture("Controls.xaml");
        XElement style = Assert.Single(
            controls.Descendants(Presentation + "Style"),
            element => element.Attribute(X + "Key")?.Value == "DangerButtonStyle");

        AssertStyleStateSetter(style, "IsMouseOver", "DangerButtonHoverBrush");
        AssertStyleStateSetter(style, "IsPressed", "DangerButtonPressedBrush");
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

    private static void AssertStyleStateSetter(
        XElement style,
        string state,
        string resourceKey)
    {
        XElement trigger = Assert.Single(
            style.Elements(Presentation + "Style.Triggers")
                .Elements(Presentation + "Trigger"),
            element => element.Attribute("Property")?.Value == state &&
                element.Attribute("Value")?.Value == "True");
        Assert.Contains(trigger.Elements(Presentation + "Setter"), setter =>
            setter.Attribute("Property")?.Value == "Background" &&
            setter.Attribute("Value")?.Value.Contains(resourceKey, StringComparison.Ordinal) == true);
    }

    private static void AssertTemplateStateSetter(
        XElement triggers,
        string state,
        string resourceKey)
    {
        XElement trigger = Assert.Single(
            triggers.Elements(Presentation + "Trigger"),
            element => element.Attribute("Property")?.Value == state &&
                element.Attribute("Value")?.Value == "True");
        Assert.Contains(trigger.Elements(Presentation + "Setter"), setter =>
            setter.Attribute("TargetName") is null &&
            setter.Attribute("Property")?.Value == "Background" &&
            setter.Attribute("Value")?.Value.Contains(resourceKey, StringComparison.Ordinal) == true);
    }

    private static void AssertDisconnectStateSetter(
        XElement style,
        string state,
        string resourceKey)
    {
        XElement trigger = Assert.Single(
            style.Elements(Presentation + "Style.Triggers")
                .Elements(Presentation + "MultiDataTrigger"),
            element => element.Descendants(Presentation + "Condition").Any(condition =>
                    condition.Attribute("Binding")?.Value.Contains(
                        "ConnectionActionText",
                        StringComparison.Ordinal) == true &&
                    condition.Attribute("Value")?.Value == "Disconnect") &&
                element.Descendants(Presentation + "Condition").Any(condition =>
                    condition.Attribute("Property")?.Value == state &&
                    condition.Attribute("Value")?.Value == "True"));
        Assert.Contains(trigger.Elements(Presentation + "Setter"), setter =>
            setter.Attribute("Property")?.Value == "Background" &&
            setter.Attribute("Value")?.Value.Contains(resourceKey, StringComparison.Ordinal) == true);
    }

    private static void AssertContrastAtLeast(
        string foreground,
        string background,
        double minimumRatio)
    {
        double ratio = (RelativeLuminance(foreground) + 0.05) /
            (RelativeLuminance(background) + 0.05);
        if (ratio < 1)
        {
            ratio = 1 / ratio;
        }

        Assert.True(
            ratio >= minimumRatio,
            $"Expected {foreground} on {background} to meet {minimumRatio:0.0}:1, but was {ratio:0.00}:1.");
    }

    private static double RelativeLuminance(string color)
    {
        Assert.StartsWith("#", color, StringComparison.Ordinal);
        Assert.Equal(7, color.Length);

        return (0.2126 * LinearChannel(color[1..3])) +
            (0.7152 * LinearChannel(color[3..5])) +
            (0.0722 * LinearChannel(color[5..7]));
    }

    private static double LinearChannel(string hexadecimal)
    {
        double channel = Convert.ToInt32(hexadecimal, 16) / 255d;
        return channel <= 0.04045
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }
}
