using System.Windows;
using RetwhoConnector.App.ViewModels;

namespace RetwhoConnector.App;

public partial class ConfigurationWindow : Window
{
    private readonly ConfigurationWindowViewModel _viewModel;
    private bool _loadingSecrets;

    public ConfigurationWindow(ConfigurationWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnLoaded;
        _viewModel.CloseRequested += OnCloseRequested;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _loadingSecrets = true;
        LicenseKeyBox.Password = _viewModel.LicenseKey;
        PosPasswordBox.Password = _viewModel.PosPassword;
        _loadingSecrets = false;
    }

    private void LicenseKeyBox_OnPasswordChanged(
        object sender,
        RoutedEventArgs e)
    {
        if (!_loadingSecrets)
        {
            _viewModel.LicenseKey = LicenseKeyBox.Password;
        }
    }

    private void PosPasswordBox_OnPasswordChanged(
        object sender,
        RoutedEventArgs e)
    {
        if (!_loadingSecrets)
        {
            _viewModel.PosPassword = PosPasswordBox.Password;
        }
    }

    private void OnCloseRequested(object? sender, bool? result)
    {
        _viewModel.CloseRequested -= OnCloseRequested;
        DialogResult = result;
        Close();
    }
}
