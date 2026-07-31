using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

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
    public async Task MainWindow_AppliesFocusedAndDisabledButtonVisualStates()
    {
        await staTestRunner.RunAsync(() =>
        {
            using UiTestHarness harness = new();
            MainWindow window = harness.CreateMainWindow();
            window.Measure(new Size(1120, 760));
            window.Arrange(new Rect(0, 0, 1120, 760));
            window.Show();
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

            Button focusedButton = FindButton(window, "Open connection settings");
            Assert.True(focusedButton.Focus());
            window.UpdateLayout();
            Assert.True(focusedButton.IsKeyboardFocused);
            Border focusRing = Assert.IsType<Border>(
                GetTemplate(focusedButton).FindName("FocusRing", focusedButton));
            Assert.Equal(1d, focusRing.Opacity);

            Button disabledButton = FindButton(window, "Open logs folder");
            disabledButton.IsEnabled = false;
            disabledButton.ApplyTemplate();
            window.UpdateLayout();

            Assert.False(disabledButton.IsEnabled);
            Border disabledBorder = Assert.IsType<Border>(
                GetTemplate(disabledButton).FindName("ButtonBorder", disabledButton));
            SolidColorBrush expectedBackground = Assert.IsType<SolidColorBrush>(
                disabledButton.TryFindResource("DisabledBackgroundBrush"));
            SolidColorBrush expectedForeground = Assert.IsType<SolidColorBrush>(
                disabledButton.TryFindResource("DisabledTextBrush"));
            Assert.Equal(
                expectedBackground.Color,
                Assert.IsType<SolidColorBrush>(disabledBorder.Background).Color);
            Assert.Equal(
                expectedForeground.Color,
                Assert.IsType<SolidColorBrush>(disabledButton.Foreground).Color);

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

    private static Button FindButton(MainWindow window, string automationName) =>
        Assert.Single(
            FindDescendants<Button>(window),
            candidate => AutomationProperties.GetName(candidate) == automationName);

    private static ControlTemplate GetTemplate(Button button) =>
        Assert.IsType<ControlTemplate>(button.Template);
}
