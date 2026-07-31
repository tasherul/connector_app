using System.ComponentModel;
using System.Drawing;
using RetwhoConnector.App.ViewModels;
using WpfApplication = System.Windows.Application;
using WpfWindowState = System.Windows.WindowState;
using FormsContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using FormsNotifyIcon = System.Windows.Forms.NotifyIcon;
using FormsToolStripMenuItem = System.Windows.Forms.ToolStripMenuItem;
using FormsToolStripSeparator = System.Windows.Forms.ToolStripSeparator;

namespace RetwhoConnector.App.Services;

public sealed class TrayIconService : ITrayIconService
{
    private readonly MainWindowViewModel _viewModel;
    private FormsNotifyIcon? _notifyIcon;
    private FormsContextMenuStrip? _menu;
    private FormsToolStripMenuItem? _connectionItem;
    private MainWindow? _window;
    private int _disposed;

    public TrayIconService(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    public void Initialize(MainWindow window)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        ArgumentNullException.ThrowIfNull(window);
        if (_notifyIcon is not null)
        {
            throw new InvalidOperationException(
                "The notification icon is already initialized.");
        }

        _window = window;
        var showItem = new FormsToolStripMenuItem("Show");
        showItem.Click += (_, _) => ShowMainWindow();
        var settingsItem = new FormsToolStripMenuItem("Settings");
        settingsItem.Click += (_, _) =>
            RunOnUiThread(() =>
            {
                ShowMainWindow();
                _viewModel.OpenSettingsCommand.Execute(null);
            });
        _connectionItem = new FormsToolStripMenuItem(
            _viewModel.ConnectionActionText);
        _connectionItem.Click += (_, _) =>
            RunOnUiThread(() =>
                _viewModel.ToggleConnectionCommand.Execute(null));
        var exitItem = new FormsToolStripMenuItem("Exit");
        exitItem.Click += (_, _) =>
            RunOnUiThread(() =>
                _viewModel.ExitCommand.Execute(null));

        _menu = new FormsContextMenuStrip();
        _menu.Items.AddRange(
        [
            showItem,
            settingsItem,
            _connectionItem,
            new FormsToolStripSeparator(),
            exitItem,
        ]);
        _menu.Opening += OnMenuOpening;
        _notifyIcon = new FormsNotifyIcon
        {
            ContextMenuStrip = _menu,
            Icon = SystemIcons.Application,
            Text = "Hybrid Edge Connector Agent",
            Visible = true,
        };
        _notifyIcon.DoubleClick += OnDoubleClick;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public void ShowMainWindow() =>
        RunOnUiThread(() =>
        {
            MainWindow? window = _window;
            if (window is null)
            {
                return;
            }

            window.ShowInTaskbar = true;
            window.Show();
            if (window.WindowState == WpfWindowState.Minimized)
            {
                window.WindowState = WpfWindowState.Normal;
            }

            window.Activate();
        });

    private void OnDoubleClick(object? sender, EventArgs args) =>
        ShowMainWindow();

    private void OnMenuOpening(object? sender, CancelEventArgs args) =>
        UpdateConnectionText();

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName ==
            nameof(MainWindowViewModel.ConnectionActionText))
        {
            RunOnUiThread(UpdateConnectionText);
        }
    }

    private void UpdateConnectionText()
    {
        if (_connectionItem is not null)
        {
            _connectionItem.Text =
                _viewModel.ConnectionActionText;
        }
    }

    private static void RunOnUiThread(Action action)
    {
        WpfApplication? application = WpfApplication.Current;
        if (application is null ||
            application.Dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _ = application.Dispatcher.InvokeAsync(action);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        if (_notifyIcon is not null)
        {
            _notifyIcon.DoubleClick -= OnDoubleClick;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        if (_menu is not null)
        {
            _menu.Opening -= OnMenuOpening;
            _menu.Dispose();
            _menu = null;
        }

        _connectionItem = null;
        _window = null;
    }
}
