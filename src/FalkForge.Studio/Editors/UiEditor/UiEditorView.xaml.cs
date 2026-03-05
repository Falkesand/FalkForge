using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace FalkForge.Studio.Editors.UiEditor;

public partial class UiEditorView : UserControl
{
    private UiEditorViewModel ViewModel => (UiEditorViewModel)DataContext;
    public UiEditorView() { InitializeComponent(); }

    private void BrowseLicense_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "RTF files (*.rtf)|*.rtf|All files (*.*)|*.*", Title = "Select license file" };
        if (dialog.ShowDialog() == true) ViewModel.LicenseFile = dialog.FileName;
    }
}
