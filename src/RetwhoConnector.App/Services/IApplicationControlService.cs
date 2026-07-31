namespace RetwhoConnector.App.Services;

public interface IApplicationControlService
{
    bool IsExitRequested { get; }

    void OpenLogsFolder();
    Task RequestExitAsync();
}
