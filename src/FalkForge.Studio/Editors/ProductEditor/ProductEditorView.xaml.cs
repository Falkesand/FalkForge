using System.Windows;
using System.Windows.Controls;

namespace FalkForge.Studio.Editors.ProductEditor;

public partial class ProductEditorView : UserControl
{
    public ProductEditorView()
    {
        InitializeComponent();
    }

    private void BrowseLicense_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "License files (*.rtf;*.txt)|*.rtf;*.txt|All files (*.*)|*.*",
            Title = "Select License File"
        };
        if (dialog.ShowDialog() == true && DataContext is ProductEditorViewModel vm)
            vm.LicenseFile = dialog.FileName;
    }
}
