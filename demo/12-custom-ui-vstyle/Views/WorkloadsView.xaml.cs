namespace CustomUiVsStyle.Views;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CustomUiVsStyle.Models;
using CustomUiVsStyle.Pages;

public partial class WorkloadsView : UserControl
{
    public WorkloadsView()
    {
        InitializeComponent();
    }

    private void Workload_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Workload workload
            && DataContext is WorkloadsPage page)
        {
            page.SelectedWorkload = workload;
        }
    }
}
