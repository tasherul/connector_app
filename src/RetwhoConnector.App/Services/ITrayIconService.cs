namespace RetwhoConnector.App.Services;

public interface ITrayIconService : IDisposable
{
    void Initialize(MainWindow window);
    void ShowMainWindow();
}
