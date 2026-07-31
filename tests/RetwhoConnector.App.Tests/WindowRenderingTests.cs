using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace RetwhoConnector.App.Tests;

[Collection(StaTestCollection.CollectionName)]
public sealed class WindowRenderingTests(StaTestRunner staTestRunner)
{
    [Fact]
    public async Task MainWindow_RendersFourContrastingStatusSegments()
    {
        await staTestRunner.RunAsync(() =>
        {
            using UiTestHarness harness = new();
            MainWindow window = harness.CreateMainWindow();
            window.Measure(new Size(1120, 760));
            window.Arrange(new Rect(0, 0, 1120, 760));
            window.UpdateLayout();

            Assert.NotNull(window.FindName("StatusRail"));
            Assert.Equal(4, harness.FindStatusSegments(window).Count);
            harness.AssertTextHasContrastingBackground(window);

            harness.RequestExit();
            window.Close();
        });
    }

    [Fact]
    public async Task MainWindow_UsesTwoStatusColumnsAtNarrowWidth()
    {
        await staTestRunner.RunAsync(() =>
        {
            using UiTestHarness harness = new();
            MainWindow window = harness.CreateMainWindow();
            window.Width = 840;
            window.Measure(new Size(840, 760));
            window.Arrange(new Rect(0, 0, 840, 760));
            window.UpdateLayout();

            UniformGrid statusRail = Assert.IsType<UniformGrid>(
                window.FindName("StatusRailGrid"));
            Assert.Equal(2, statusRail.Columns);

            harness.RequestExit();
            window.Close();
        });
    }

    [Fact]
    public async Task MainWindow_ProvidesStyledButtonResourcesAndFocusTemplate()
    {
        await staTestRunner.RunAsync(() =>
        {
            using UiTestHarness harness = new();
            MainWindow window = harness.CreateMainWindow();
            window.Measure(new Size(1120, 760));
            window.Arrange(new Rect(0, 0, 1120, 760));
            window.UpdateLayout();

            string[] actionNames =
            [
                "Open connection settings",
                "Connect or disconnect",
                "Open logs folder",
                "Exit application",
            ];
            foreach (string actionName in actionNames)
            {
                Button button = Assert.Single(
                    FindDescendants<Button>(window),
                    candidate => AutomationProperties.GetName(candidate) == actionName);
                button.ApplyTemplate();
                Assert.NotNull(button.Style);
                Assert.NotNull(button.Template.FindName("FocusRing", button));
                Assert.NotNull(button.TryFindResource("FocusRingBrush"));
                Assert.NotNull(button.TryFindResource("DisabledBackgroundBrush"));
                Assert.NotNull(button.TryFindResource("DisabledTextBrush"));
            }

            harness.RequestExit();
            window.Close();
        });
    }

    [Fact]
    public async Task ConfigurationWindow_PreservesPasswordBoxesAndContrastingText()
    {
        await staTestRunner.RunAsync(() =>
        {
            using UiTestHarness harness = new();
            ConfigurationWindow window = harness.CreateConfigurationWindow();
            window.Measure(new Size(540, 620));
            window.Arrange(new Rect(0, 0, 540, 620));
            window.UpdateLayout();

            harness.AssertTextHasContrastingBackground(
                window,
                "Connection & License Configuration",
                "Settings are tested before encrypted storage is changed.",
                "ValidationMessage");
            Assert.IsType<PasswordBox>(window.FindName("LicenseKeyBox"));
            Assert.IsType<PasswordBox>(window.FindName("PosPasswordBox"));

            window.Close();
        });
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (int index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
             index++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T descendant in FindDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
