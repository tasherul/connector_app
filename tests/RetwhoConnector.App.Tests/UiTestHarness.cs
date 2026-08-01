using System.Net.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RetwhoConnector.App.Services;
using RetwhoConnector.App.ViewModels;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Models;
using RetwhoConnector.Core.Services;

namespace RetwhoConnector.App.Tests;

public sealed class UiTestHarness : IDisposable
{
    private readonly FakeApplicationControlService _applicationControl = new();
    private readonly FakeAgentOrchestrationService _orchestration = new();
    private readonly FakeAgentLog _agentLog = new();
    private readonly UiLogBufferSink _activity = new();
    private readonly FakeConfigurationDialogService _configurationDialog = new();
    private readonly FakeUserDialogService _dialogs = new();
    private readonly List<Window> _windows = [];

    public MainWindow CreateMainWindow()
    {
        MainWindowViewModel viewModel = new(
            _orchestration,
            _agentLog,
            _activity,
            _configurationDialog,
            _applicationControl,
            _dialogs);
        return Track(new MainWindow(viewModel, _applicationControl));
    }

    public ConfigurationWindow CreateConfigurationWindow()
    {
        ConfigurationWindowViewModel viewModel = new(_orchestration, _dialogs);
        return Track(new ConfigurationWindow(viewModel));
    }

    public void ShowForRendering(Window window, Size size)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.Width = size.Width;
        window.Height = size.Height;
        window.Show();
        window.Measure(size);
        window.Arrange(new Rect(0, 0, size.Width, size.Height));
        window.UpdateLayout();
    }

    public IReadOnlyList<ContentControl> FindStatusSegments(MainWindow window) =>
        FindDescendants<ContentControl>(window)
            .Where(control => !string.IsNullOrWhiteSpace(
                System.Windows.Automation.AutomationProperties.GetName(control)))
            .Where(control => System.Windows.Automation.AutomationProperties.GetName(control)
                .EndsWith("status indicator", StringComparison.Ordinal))
            .ToArray();

    public void AssertTextHasContrastingBackground(FrameworkElement root)
    {
        TextBlock[] textBlocks = FindDescendants<TextBlock>(root).ToArray();
        Assert.NotEmpty(textBlocks);

        foreach (TextBlock textBlock in textBlocks.Where(block =>
                     !string.IsNullOrWhiteSpace(block.Text)))
        {
            AssertBrushesDiffer(
                ResolveBrush(textBlock.Foreground, "PrimaryTextBrush"),
                FindBackground(textBlock));
        }
    }

    public void AssertTextHasContrastingBackground(
        FrameworkElement root,
        params string[] textValues)
    {
        foreach (string textValue in textValues)
        {
            TextBlock textBlock = Assert.Single(
                FindDescendants<TextBlock>(root),
                block => block.Text == textValue ||
                    (textValue == "ValidationMessage" &&
                     block.GetBindingExpression(TextBlock.TextProperty) is not null));
            AssertBrushesDiffer(
                ResolveBrush(textBlock.Foreground, "PrimaryTextBrush"),
                FindBackground(textBlock));
        }
    }

    public void RequestExit() => _applicationControl.RequestExit();

    public void SetConnectorStatus(ConnectorStatus status) =>
        _orchestration.SetStatus(status);

    public void Dispose()
    {
        RequestExit();
        foreach (Window window in _windows.ToArray())
        {
            window.Close();
        }

        _windows.Clear();
    }

    private TWindow Track<TWindow>(TWindow window)
        where TWindow : Window
    {
        _windows.Add(window);
        window.Closed += (_, _) => _windows.Remove(window);
        return window;
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
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

    private static SolidColorBrush ResolveBrush(Brush? brush, string resourceKey)
    {
        Brush resolved = brush ?? (Brush)Application.Current.Resources[resourceKey];
        return Assert.IsType<SolidColorBrush>(resolved);
    }

    private static SolidColorBrush FindBackground(DependencyObject element)
    {
        for (DependencyObject? current = element; current is not null; current =
                 VisualTreeHelper.GetParent(current))
        {
            Brush? brush = current switch
            {
                Border border => border.Background,
                Panel panel => panel.Background,
                Control control => control.Background,
                _ => null,
            };

            if (brush is not null)
            {
                return ResolveBrush(brush, "WindowBackgroundBrush");
            }
        }

        return ResolveBrush(null, "WindowBackgroundBrush");
    }

    private static void AssertBrushesDiffer(
        SolidColorBrush foreground,
        SolidColorBrush background) =>
        Assert.NotEqual(foreground.Color, background.Color);

    private sealed class FakeAgentOrchestrationService : IAgentOrchestrationService
    {
        public ConnectorStatus CurrentStatus { get; private set; } = new()
        {
            PosConfiguration = PosConfigurationState.Configured,
            BridgeTransport = BridgeTransportState.Connected,
            AgentRegistration = AgentRegistrationState.Registered,
        };

        public ConnectorSettings? CurrentSettings => null;

        public event EventHandler<ConnectorStatus>? StatusChanged;

        public event EventHandler<VdatetimeResult>? ResultReceived
        {
            add { }
            remove { }
        }

        public Task InitializeAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<ConnectorSettings?> LoadSettingsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ConnectorSettings?>(null);

        public Task SaveTestAndConnectAsync(
            ConnectorSettings settings,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ConnectSavedAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DisconnectAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<PresentedCertificate> InspectCertificateAsync(
            string posBaseUrl,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PresentedCertificate
            {
                Subject = "CN=fake",
                Issuer = "CN=fake",
                ValidFromUtc = DateTimeOffset.UnixEpoch,
                ValidToUtc = DateTimeOffset.UnixEpoch.AddDays(1),
                Sha256Fingerprint = "00",
                PolicyErrors = SslPolicyErrors.None,
            });

        public Task ClearSettingsAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void SetStatus(ConnectorStatus status)
        {
            CurrentStatus = status;
            StatusChanged?.Invoke(this, status);
        }
    }

    private sealed class FakeAgentLog : IAgentLog
    {
        public LogPipelineHealth CurrentHealth { get; } = new(
            LoggingHealthState.Healthy,
            0,
            "Fake local logs are healthy");

        public event EventHandler<LogPipelineHealth>? HealthChanged
        {
            add { }
            remove { }
        }

        public bool TryWrite(
            AgentLogLevel level,
            AgentLogCategory category,
            string message,
            string? details = null,
            string? correlationId = null) => true;
    }

    private sealed class FakeConfigurationDialogService : IConfigurationDialogService
    {
        public Task ShowAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FakeApplicationControlService : IApplicationControlService
    {
        public bool IsExitRequested { get; private set; }

        public void OpenLogsFolder()
        {
        }

        public Task RequestExitAsync()
        {
            RequestExit();
            return Task.CompletedTask;
        }

        public void RequestExit() => IsExitRequested = true;
    }

    private sealed class FakeUserDialogService : IUserDialogService
    {
        public bool ConfirmCertificate(PresentedCertificate certificate) => false;

        public bool ConfirmClearSettings() => false;

        public void ShowError(string message)
        {
        }
    }
}
