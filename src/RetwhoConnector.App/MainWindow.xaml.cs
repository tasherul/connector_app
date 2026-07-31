using System.ComponentModel;
using System.Windows;
using RetwhoConnector.App.Services;
using RetwhoConnector.App.ViewModels;

namespace RetwhoConnector.App;

public partial class MainWindow : Window
{
    private readonly IApplicationControlService _applicationControl;

    public MainWindow(
        MainWindowViewModel viewModel,
        IApplicationControlService applicationControl)
    {
        InitializeComponent();
        _applicationControl = applicationControl;
        DataContext = viewModel;
        Closing += OnClosing;
        StateChanged += OnStateChanged;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_applicationControl.IsExitRequested)
        {
            return;
        }

        e.Cancel = true;
        ShowInTaskbar = false;
        Hide();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized ||
            _applicationControl.IsExitRequested)
        {
            return;
        }

        ShowInTaskbar = false;
        Hide();
    }
}
