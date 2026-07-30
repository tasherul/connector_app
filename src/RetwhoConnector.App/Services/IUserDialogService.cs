using RetwhoConnector.Core.Models;

namespace RetwhoConnector.App.Services;

public interface IUserDialogService
{
    bool ConfirmCertificate(PresentedCertificate certificate);
    bool ConfirmClearSettings();
    void ShowError(string message);
}
