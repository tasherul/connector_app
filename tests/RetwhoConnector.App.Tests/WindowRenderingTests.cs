using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using RetwhoConnector.Core.Models;

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
            harness.ShowForRendering(window, new Size(1120, 760));

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
            harness.ShowForRendering(window, new Size(840, 760));

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
            harness.ShowForRendering(window, new Size(1120, 760));

            string[] actionNames =
            [
                "Open connection settings",
                "Connect",
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

            Button connectionButton = FindButton(window, "Connect");
            harness.SetConnectorStatus(new ConnectorStatus
            {
                PosConfiguration = PosConfigurationState.Configured,
                PosAuthentication = PosAuthenticationState.Authenticated,
                BridgeTransport = BridgeTransportState.Connected,
                AgentRegistration = AgentRegistrationState.Registered,
                Message = "Connected.",
            });
            window.UpdateLayout();
            Assert.Same(connectionButton, FindButton(window, "Disconnect"));

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
            harness.ShowForRendering(window, new Size(540, 620));

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
