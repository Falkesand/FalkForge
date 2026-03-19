using System.Windows;
using System.Windows.Controls;
using FalkForge.Studio.Project;
using Microsoft.Win32;

namespace FalkForge.Studio.Export;

public partial class CiCdExportDialog : Window
{
    private readonly StudioProject _project;

    public CiCdExportDialog(StudioProject project)
    {
        _project = project;
        InitializeComponent();
        UpdatePreview();
    }

    private CiCdPlatform SelectedPlatform => PlatformComboBox.SelectedIndex switch
    {
        0 => CiCdPlatform.GitHubActions,
        1 => CiCdPlatform.AzureDevOps,
        2 => CiCdPlatform.Jenkins,
        _ => CiCdPlatform.GitHubActions
    };

    private void UpdatePreview()
    {
        var result = CiCdExporter.Export(_project, SelectedPlatform);
        PreviewTextBox.Text = result.IsSuccess
            ? result.Value
            : $"Error: {result.Error.Message}";
    }

    private void PlatformComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PreviewTextBox is not null)
            UpdatePreview();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(PreviewTextBox.Text);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var (filter, fileName) = SelectedPlatform switch
        {
            CiCdPlatform.GitHubActions => ("YAML files (*.yml)|*.yml|All files (*.*)|*.*", "build-installer.yml"),
            CiCdPlatform.AzureDevOps => ("YAML files (*.yml)|*.yml|All files (*.*)|*.*", "azure-pipelines.yml"),
            CiCdPlatform.Jenkins => ("Jenkinsfile (*.*)|*.*|All files (*.*)|*.*", "Jenkinsfile"),
            _ => ("All files (*.*)|*.*", "pipeline")
        };

        var dialog = new SaveFileDialog
        {
            Filter = filter,
            Title = "Save CI/CD Pipeline",
            FileName = fileName
        };

        if (dialog.ShowDialog() == true)
        {
            System.IO.File.WriteAllText(dialog.FileName, PreviewTextBox.Text);
            DialogResult = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
