using System.Diagnostics;
using System.IO;
using RetwhoConnector.Core.Abstractions;
using RetwhoConnector.Core.Configuration;
using RetwhoConnector.Core.Models;
using WpfApplication = System.Windows.Application;

namespace RetwhoConnector.App.Services;

public sealed class ApplicationControlService(
    LogStorageOptions logStorageOptions,
    IAgentOrchestrationService orchestration,
    IAgentLog agentLog) : IApplicationControlService
{
    private int _exitRequested;

    public bool IsExitRequested =>
        Volatile.Read(ref _exitRequested) != 0;

    public void OpenLogsFolder()
    {
        Directory.CreateDirectory(logStorageOptions.LogDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = logStorageOptions.LogDirectory,
            UseShellExecute = true,
        });
    }

    public async Task RequestExitAsync()
    {
        if (Interlocked.Exchange(ref _exitRequested, 1) != 0)
        {
            return;
        }

        agentLog.TryWrite(
            AgentLogLevel.Information,
            AgentLogCategory.General,
            "Agent shutdown requested.");
        using var disconnectSource =
            new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await orchestration.DisconnectAsync(
                disconnectSource.Token);
        }
        catch (OperationCanceledException)
            when (disconnectSource.IsCancellationRequested)
        {
            agentLog.TryWrite(
                AgentLogLevel.Warning,
                AgentLogCategory.Error,
                "Cloud disconnect timed out during shutdown.");
        }
        catch (Exception exception)
        {
            agentLog.TryWrite(
                AgentLogLevel.Warning,
                AgentLogCategory.Error,
                "Cloud disconnect failed during shutdown.",
                exception.GetType().FullName);
        }

        WpfApplication? application = WpfApplication.Current;
        if (application is null)
        {
            return;
        }

        if (application.Dispatcher.CheckAccess())
        {
            application.Shutdown();
        }
        else
        {
            await application.Dispatcher.InvokeAsync(
                application.Shutdown);
        }
    }
}
