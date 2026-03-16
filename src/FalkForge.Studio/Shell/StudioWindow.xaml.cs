using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FalkForge.Studio.Navigation;
using Microsoft.Win32;

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
    {
        var dialog = new NewProjectDialog { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedTemplate is not null)
            ViewModel.NewProject(dialog.SelectedTemplate);
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "FalkForge Studio Project (*.ffstudio)|*.ffstudio|All files (*.*)|*.*",
            Title = "Open Project"
        };
        if (dialog.ShowDialog() == true)
            ViewModel.LoadProject(dialog.FileName);
    }

    private void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ProjectPath is null)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "FalkForge Studio Project (*.ffstudio)|*.ffstudio",
                Title = "Save Project"
            };
            if (dialog.ShowDialog() == true)
                ViewModel.SaveProject(dialog.FileName);
        }
        else
        {
            ViewModel.SaveProject();
        }
    }

    private async void Build_Click(object sender, RoutedEventArgs e)
    {
        var baseDirectory = ViewModel.ProjectPath is not null
            ? System.IO.Path.GetDirectoryName(ViewModel.ProjectPath) ?? "."
            : ".";
        await ViewModel.BuildAsync(baseDirectory);
    }

    private void ValidationItem_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid grid && grid.SelectedItem is ValidationMessage msg && msg.EditorKey is not null)
            ViewModel.NavigateTo(msg.EditorKey);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}
