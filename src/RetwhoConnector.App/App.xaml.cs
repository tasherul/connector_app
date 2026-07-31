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
using RetwhoConnector.Core.Models;
using RetwhoConnector.Core.Security;
using RetwhoConnector.Core.Services;
using Serilog;
using WpfMessageBox = System.Windows.MessageBox;

namespace RetwhoConnector.App;

public partial class App : System.Windows.Application
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
            WpfMessageBox.Show(
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
            "startup-.log");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                retainedFileCountLimit: 14,
                shared: false,
                outputTemplate:
                    "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] " +
                    "{Message:lj}{NewLine}")
            .CreateLogger();

        string startupStage = "building application services";
        try
        {
            IHost host = BuildHost();
            _host = host;

            startupStage = "starting application services";
            await host.StartAsync(_applicationSource.Token);

            startupStage = "loading saved settings";
            MainWindowViewModel viewModel =
                host.Services.GetRequiredService<MainWindowViewModel>();
            await viewModel.InitializeAsync(_applicationSource.Token);

            startupStage = "creating the main window";
            MainWindow window = await Dispatcher.InvokeAsync(
                () => host.Services.GetRequiredService<MainWindow>());

            startupStage = "showing the main window";
            await Dispatcher.InvokeAsync(() =>
            {
                MainWindow = window;
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                window.Show();
            });
        }
        catch (Exception exception)
        {
            string errorCode = $"0x{exception.HResult:X8}";
            string exceptionTarget =
                exception.TargetSite?.DeclaringType?.FullName is string typeName
                    ? $"{typeName}.{exception.TargetSite.Name}"
                    : "unknown";
            _host?.Services.GetService<IAgentLog>()?.TryWrite(
                AgentLogLevel.Critical,
                AgentLogCategory.Error,
                "Application startup failed.",
                $"{exception.GetType().Name} at {exceptionTarget} ({errorCode})");
            Log.Fatal(
                "Application startup failed at {StartupStage} " +
                "({ExceptionType}, {ExceptionTarget}, {ErrorCode})",
                startupStage,
                exception.GetType().Name,
                exceptionTarget,
                errorCode);
            WpfMessageBox.Show(
                $"Retwho Connector could not start while {startupStage}.\n\n" +
                $"Error: {exception.GetType().Name} ({errorCode})\n" +
                "See the local log for the safe diagnostic target.",
                "Retwho Connector",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private IHost BuildHost() =>
        Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureServices(services =>
            {
                services.AddSingleton(new BridgeOptions());
                services.AddSingleton(new PosOptions());
                services.AddSingleton(new AgentLoggingOptions());
                services.AddSingleton(new LogStorageOptions());
                services.AddSingleton(TimeProvider.System);
                services.AddSingleton<ILogSanitizer, LogSanitizer>();
                services.AddSingleton<IAgentLogSink>(provider =>
                    new RollingFileLogSink(
                        provider.GetRequiredService<LogStorageOptions>(),
                        provider.GetRequiredService<TimeProvider>()));
                services.AddSingleton<IAgentLogSink>(provider =>
                    new SqliteLogSink(
                        provider.GetRequiredService<LogStorageOptions>(),
                        provider.GetRequiredService<TimeProvider>()));
                services.AddSingleton<AgentLogPipeline>();
                services.AddSingleton<IAgentLog>(provider =>
                    provider.GetRequiredService<AgentLogPipeline>());
                services.AddSingleton<ILoggerProvider, ChannelLoggerProvider>();
                services.AddSingleton<IHostedService>(provider =>
                    provider.GetRequiredService<AgentLogPipeline>());
                services.AddSingleton<ISecureSettingsService, SecureSettingsService>();
                services.AddSingleton<ICertificateTrustService, CertificateTrustService>();
                services.AddSingleton<IPosResponseReader, PosResponseReader>();
                services.AddSingleton<IVdatetimeXmlMapper, VdatetimeXmlMapper>();
                services.AddSingleton<PosHttpRequestFactory>();
                services.AddHttpClient<PosHttpClient>()
                    .ConfigurePrimaryHttpMessageHandler(provider =>
                        PosHttpClientHandlerFactory.Create(
                            provider.GetRequiredService<ICertificateTrustService>()));
                services.AddSingleton<IPosHttpClient>(provider =>
                    provider.GetRequiredService<PosHttpClient>());
                services.AddSingleton<
                    IPosAuthenticationService,
                    PosAuthenticationService>();
                services.AddSingleton<IPosDataService, PosDataService>();
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
                services.AddSingleton<AgentOrchestrationService>();
                services.AddSingleton<IAgentOrchestrationService>(provider =>
                    provider.GetRequiredService<AgentOrchestrationService>());
                services.AddSingleton<IHostedService>(provider =>
                    provider.GetRequiredService<AgentOrchestrationService>());
                services.AddSingleton<IUserDialogService, UserDialogService>();
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

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
                if (_host is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync();
                }
                else
                {
                    _host.Dispose();
                }
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
