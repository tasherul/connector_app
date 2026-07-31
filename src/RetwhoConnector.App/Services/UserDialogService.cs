using System.Windows;
using RetwhoConnector.Core.Models;
using WpfMessageBox = System.Windows.MessageBox;

namespace RetwhoConnector.App.Services;

public sealed class UserDialogService : IUserDialogService
{
    public bool ConfirmCertificate(PresentedCertificate certificate)
    {
        string message =
            "Verify this POS certificate with your administrator before approval.\n\n" +
            $"Subject: {certificate.Subject}\n" +
            $"Issuer: {certificate.Issuer}\n" +
            $"Valid from: {certificate.ValidFromUtc:u}\n" +
            $"Valid to: {certificate.ValidToUtc:u}\n" +
            $"SHA-256: {certificate.Sha256Fingerprint}\n\n" +
            "Trust this exact certificate for this POS address?";
        return WpfMessageBox.Show(
            message,
            "Trust POS Certificate",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    public bool ConfirmClearSettings() =>
        WpfMessageBox.Show(
            "Clear all encrypted connector settings and disconnect?",
            "Clear Saved Settings",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    public void ShowError(string message) =>
        WpfMessageBox.Show(
            message,
            "Hybrid Edge Connector Agent",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
}
