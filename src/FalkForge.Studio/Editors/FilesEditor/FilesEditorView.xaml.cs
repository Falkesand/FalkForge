using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace FalkForge.Studio.Editors.FilesEditor;

public partial class FilesEditorView : UserControl
{
    private FilesEditorViewModel ViewModel => (FilesEditorViewModel)DataContext;

    public FilesEditorView()
    {
        InitializeComponent();
    }

    private void AddFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Title = "Select files to include"
        };
        if (dialog.ShowDialog() == true)
        {
            var featureId = "Main";
            foreach (var file in dialog.FileNames)
                ViewModel.AddFile(file, featureId);
        }
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select folder to add"
        };
        if (dialog.ShowDialog() == true)
        {
            var featureId = ViewModel.Files.Count > 0
                ? ViewModel.Files[0].FeatureId
                : "Main";
            foreach (var file in System.IO.Directory.GetFiles(dialog.FolderName))
                ViewModel.AddFile(file, featureId);
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
        => ViewModel.RemoveSelected();
}
