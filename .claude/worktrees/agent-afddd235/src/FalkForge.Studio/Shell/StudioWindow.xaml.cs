using System.Windows;
using System.Windows.Controls;
using FalkForge.Studio.Navigation;

namespace FalkForge.Studio.Shell;

public partial class StudioWindow : Window
{
    private StudioViewModel ViewModel => (StudioViewModel)DataContext;

    public StudioWindow()
    {
        InitializeComponent();
        DataContext = new StudioViewModel();
    }

    private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeNodeViewModel node)
            ViewModel.NavigateTo(node.NodeKey);
    }

    private void NewProject_Click(object sender, RoutedEventArgs e)
        => ViewModel.OutputText = "New project created.";

    private void OpenProject_Click(object sender, RoutedEventArgs e) { }
    private void SaveProject_Click(object sender, RoutedEventArgs e) { }
    private void Build_Click(object sender, RoutedEventArgs e)
    {
        var baseDir = Environment.CurrentDirectory;
        ViewModel.Build(baseDir);
    }
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}
