using System.Windows;
using System.Windows.Controls;
using MAS.Pages;

namespace MAS.Views;

public partial class AdvancedInstallDirMultiServerExView : UserControl
{
    public AdvancedInstallDirMultiServerExView()
    {
        InitializeComponent();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is AdvancedInstallDirMultiServerExPage page)
            page.BrowseFolder();
    }
}