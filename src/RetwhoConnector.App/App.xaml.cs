using System.IO;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RetwhoConnector.App.Services;
using RetwhoConnector.App.ViewModels;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Configuration;
using RetwhoConnector.Core.Services;
using Serilog;

namespace RetwhoConnector.App;

public partial class App : Application
{
    private readonly CancellationTokenSource _applicationSource = new();
    private IHost? _host;
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            name: @"Local\RetwhoConnector.SingleInstance",
            createdNew: out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "Retwho Connector is already running.",
                "Retwho Connector",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }
        _ownsSingleInstanceMutex = true;

        string localData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        string logPath = Path.Combine(
            localData,
            "RetwhoConnector",
            "Logs",
            "connector-.log");

        _host = Host.CreateDefaultBuilder()
            .UseSerilog((_, _, configuration) =>
                configuration
                    .MinimumLevel.Information()
                    .WriteTo.File(
                        logPath,
                        rollingInterval: RollingInterval.Day,
                        rollOnFileSizeLimit: true,
                        fileSizeLimitBytes: 10 * 1024 * 1024,
                        retainedFileCountLimit: 10,
                        shared: false,
                        outputTemplate:
                            "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] " +
                            "{Message:lj}{NewLine}{Exception}"))
            .ConfigureServices(services =>
            {
                services.AddSingleton(new BridgeOptions());
                services.AddSingleton(new PosOptions());
                services.AddSingleton(TimeProvider.System);
                services.AddSingleton<ISecureSettingsService, SecureSettingsService>();
                services.AddSingleton<ICertificateTrustService, CertificateTrustService>();
                services.AddSingleton<IPosResponseReader, PosResponseReader>();
                services.AddSingleton<IVdatetimeXmlMapper, VdatetimeXmlMapper>();
                services.AddSingleton<PosHttpRequestFactory>();
                services.AddHttpClient<IPosAuthenticationService, PosAuthenticationService>()
                    .ConfigurePrimaryHttpMessageHandler(provider =>
                        PosHttpClientHandlerFactory.Create(
                            provider.GetRequiredService<ICertificateTrustService>()));
                services.AddHttpClient<IPosDataService, PosDataService>()
                    .ConfigurePrimaryHttpMessageHandler(provider =>
                        PosHttpClientHandlerFactory.Create(
                            provider.GetRequiredService<ICertificateTrustService>()));
                services.AddSingleton<IBridgeSocketClient, BridgeSocketClient>();
                services.AddSingleton<IActionExecutionRegistry>(provider =>
                    new ActionExecutionRegistry(
                        provider.GetRequiredService<TimeProvider>(),
                        _applicationSource.Token));
                services.AddSingleton(provider =>
                    new ConnectorCoordinator(
                        provider.GetRequiredService<ISecureSettingsService>(),
                        provider.GetRequiredService<IPosAuthenticationService>(),
                        provider.GetRequiredService<IPosDataService>(),
                        provider.GetRequiredService<IBridgeSocketClient>(),
                        provider.GetRequiredService<IActionExecutionRegistry>(),
                        provider.GetRequiredService<BridgeOptions>(),
                        provider.GetRequiredService<TimeProvider>(),
                        provider.GetRequiredService<ILogger<ConnectorCoordinator>>(),
                        _applicationSource.Token));
                services.AddSingleton<IUserDialogService, UserDialogService>();
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        try
        {
            await _host.StartAsync(_applicationSource.Token);
            MainWindowViewModel viewModel =
                _host.Services.GetRequiredService<MainWindowViewModel>();
            await viewModel.InitializeAsync(_applicationSource.Token);
            MainWindow window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            Log.Fatal(
                "Application startup failed ({ExceptionType})",
                exception.GetType().Name);
            MessageBox.Show(
                "Retwho Connector could not start. See the local log for details.",
                "Retwho Connector",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _applicationSource.Cancel();
        if (_host is not null)
        {
            try
            {
                await _host.StopAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                _host.Dispose();
            }
        }

        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        _singleInstanceMutex?.Dispose();
        _applicationSource.Dispose();
        await Log.CloseAndFlushAsync();
        base.OnExit(e);
    }
}
