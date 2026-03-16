using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FalkForge.Studio.Export;
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

    private void ImportMsi_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "MSI Installer (*.msi)|*.msi|All files (*.*)|*.*",
            Title = "Import MSI"
        };
        if (dialog.ShowDialog() == true)
            ViewModel.ImportMsi(dialog.FileName);
    }

    private void ImportWix_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "WiX Source (*.wxs)|*.wxs|XML files (*.xml)|*.xml|All files (*.*)|*.*",
            Title = "Import WiX Source"
        };
        if (dialog.ShowDialog() == true)
            ViewModel.ImportWix(dialog.FileName);
    }

    private void ExportCSharp_Click(object sender, RoutedEventArgs e)
    {
        var result = ViewModel.ExportCSharpScript();
        if (result.IsFailure)
        {
            MessageBox.Show(result.Error.Message, "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "C# Script (*.csx)|*.csx|C# file (*.cs)|*.cs|All files (*.*)|*.*",
            Title = "Export to C# Script",
            FileName = "installer.csx"
        };
        if (dialog.ShowDialog() == true)
        {
            System.IO.File.WriteAllText(dialog.FileName, result.Value);
            ViewModel.OutputText = $"Exported C# script: {dialog.FileName}";
        }
    }

    private void ExportCiCd_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CiCdExportDialog(ViewModel.GetProject()) { Owner = this };
        if (dialog.ShowDialog() == true)
            ViewModel.OutputText = "CI/CD pipeline exported.";
    }

    private void CompareProjects_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.NavigateTo("diffViewer");
    }

    private void TableInspector_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.NavigateTo("tableInspector");
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}
