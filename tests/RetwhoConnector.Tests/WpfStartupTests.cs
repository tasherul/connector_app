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
}
