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
    public void LastJsonResult_DisplayUsesOneWayBinding()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "MainWindow.xaml");
        XDocument document = XDocument.Load(path);
        XNamespace presentation =
            "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        XElement resultTextBox = Assert.Single(
            document.Descendants(presentation + "TextBox"),
            element =>
                element.Attribute("Text")?.Value.Contains(
                    "LastJsonResult",
                    StringComparison.Ordinal) == true);

        Assert.Contains(
            "Mode=OneWay",
            resultTextBox.Attribute("Text")?.Value,
            StringComparison.Ordinal);
    }
}
