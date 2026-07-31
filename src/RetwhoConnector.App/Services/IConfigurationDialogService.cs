namespace RetwhoConnector.App.Services;

public interface IConfigurationDialogService
{
    Task ShowAsync(CancellationToken cancellationToken);
}
