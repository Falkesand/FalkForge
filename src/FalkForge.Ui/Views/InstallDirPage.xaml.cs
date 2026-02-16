namespace FalkForge.Ui.Views;

using System.Windows.Controls;
using FalkForge.Ui.ViewModels;
using Microsoft.Win32;

public partial class InstallDirPage : UserControl
{
    public InstallDirPage()
    {
        InitializeComponent();
    }

    private void OnBrowseClick(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Installation Directory"
        };

        if (dialog.ShowDialog() == true && DataContext is InstallDirPageViewModel vm)
        {
            vm.InstallDirectory = dialog.FolderName;
        }
    }
}
