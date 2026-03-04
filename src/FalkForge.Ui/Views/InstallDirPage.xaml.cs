using System.Windows;
using System.Windows.Controls;
using FalkForge.Ui.ViewModels;
using Microsoft.Win32;

namespace FalkForge.Ui.Views;

public partial class InstallDirPage : UserControl
{
    public InstallDirPage()
    {
        InitializeComponent();
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Installation Directory"
        };

        if (dialog.ShowDialog() == true && DataContext is InstallDirPageViewModel vm)
            vm.InstallDirectory = dialog.FolderName;
    }
}