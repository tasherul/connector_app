using System.Diagnostics;
using System.IO;
using RetwhoConnector.Core.Configuration;
using WpfApplication = System.Windows.Application;

namespace RetwhoConnector.App.Services;

public sealed class ApplicationControlService(
    LogStorageOptions logStorageOptions) : IApplicationControlService
{
    public void OpenLogsFolder()
    {
        Directory.CreateDirectory(logStorageOptions.LogDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = logStorageOptions.LogDirectory,
            UseShellExecute = true,
        });
    }

    public void RequestExit() => WpfApplication.Current.Shutdown();
}
