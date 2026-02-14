namespace FalkInstaller.Ui.Views;

using System.Windows;
using FalkInstaller.Ui.ViewModels;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(DefaultShellViewModel shell) : this()
    {
        DataContext = shell;
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is DefaultShellViewModel shell)
        {
            shell.NavigateBack();
        }
    }

    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is DefaultShellViewModel shell)
        {
            shell.NavigateNext();
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is DefaultShellViewModel shell)
        {
            shell.Engine.Cancel();
        }

        Close();
    }
}
