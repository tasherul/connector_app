using Microsoft.Extensions.DependencyInjection;
using RetwhoConnector.App.ViewModels;
using WpfApplication = System.Windows.Application;

namespace RetwhoConnector.App.Services;

public sealed class ConfigurationDialogService(
    IServiceProvider serviceProvider) : IConfigurationDialogService
{
    public async Task ShowAsync(CancellationToken cancellationToken)
    {
        ConfigurationWindowViewModel viewModel =
            serviceProvider.GetRequiredService<ConfigurationWindowViewModel>();
        await viewModel.LoadAsync(cancellationToken);
        var window = new ConfigurationWindow(viewModel)
        {
            Owner = WpfApplication.Current.MainWindow,
        };
        window.ShowDialog();
    }
}
