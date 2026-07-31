using System.Windows;
using System.Windows.Threading;

namespace RetwhoConnector.App.Tests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class StaTestCollection : ICollectionFixture<StaTestRunner>
{
    public const string CollectionName = "RetwhoConnector WPF STA";
}

public sealed class StaTestRunner : IAsyncLifetime
{
    private readonly TaskCompletionSource<Dispatcher> _dispatcherStarted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();
    private Application? _application;
    private Thread? _thread;
    private bool _started;
    private bool _disposed;

    public Task InitializeAsync()
    {
        lock (_gate)
        {
            if (_started)
            {
                return _dispatcherStarted.Task;
            }

            _started = true;
            _thread = new Thread(RunDispatcher)
            {
                IsBackground = true,
                Name = "RetwhoConnector WPF test STA",
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        return _dispatcherStarted.Task;
    }

    public async Task RunAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Dispatcher dispatcher = await _dispatcherStarted.Task;
        await dispatcher.InvokeAsync(action).Task;
    }

    public async Task DisposeAsync()
    {
        Thread? thread;
        Application? application;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            thread = _thread;
            application = _application;
        }

        if (application is not null)
        {
            await application.Dispatcher.InvokeAsync(application.Shutdown).Task;
        }

        thread?.Join();
    }

    private void RunDispatcher()
    {
        try
        {
            Application application = new()
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown,
            };
            LoadResources(application);
            _application = application;
            _dispatcherStarted.SetResult(application.Dispatcher);
            Dispatcher.Run();
        }
        catch (Exception exception)
        {
            _dispatcherStarted.TrySetException(exception);
        }
    }

    private static void LoadResources(Application application)
    {
        foreach (string resourcePath in new[]
                 {
                     "Styles/Colors.xaml",
                     "Styles/Icons.xaml",
                     "Styles/Controls.xaml",
                 })
        {
            ResourceDictionary dictionary = (ResourceDictionary)Application.LoadComponent(
                new Uri(
                    $"pack://application:,,,/RetwhoConnector;component/{resourcePath}",
                    UriKind.Absolute));
            application.Resources.MergedDictionaries.Add(dictionary);
        }
    }
}
