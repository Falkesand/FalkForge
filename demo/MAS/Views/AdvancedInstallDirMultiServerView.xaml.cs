using System.Windows;
using System.Windows.Controls;
using MAS.Pages;

namespace MAS.Views;

public partial class AdvancedInstallDirMultiServerView : UserControl
{
    public AdvancedInstallDirMultiServerView()
    {
        InitializeComponent();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is AdvancedInstallDirMultiServerPage page)
            page.BrowseFolder();
    }
}