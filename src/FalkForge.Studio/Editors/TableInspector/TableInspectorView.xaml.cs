using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace FalkForge.Studio.Editors.TableInspector;

public partial class TableInspectorView : UserControl
{
    private TableInspectorViewModel ViewModel => (TableInspectorViewModel)DataContext;

    public TableInspectorView()
    {
        InitializeComponent();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "MSI Installer (*.msi)|*.msi|All files (*.*)|*.*",
            Title = "Select MSI File to Inspect"
        };

        if (dialog.ShowDialog() == true)
            ViewModel.LoadMsiFile(dialog.FileName);
    }
}
