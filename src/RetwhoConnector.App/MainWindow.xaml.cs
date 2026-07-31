using System.Windows;
using RetwhoConnector.App.ViewModels;

namespace RetwhoConnector.App;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
