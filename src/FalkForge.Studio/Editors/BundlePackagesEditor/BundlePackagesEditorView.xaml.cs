using System.Windows;
using System.Windows.Controls;

namespace FalkForge.Studio.Editors.BundlePackagesEditor;

public partial class BundlePackagesEditorView : UserControl
{
    private BundlePackagesEditorViewModel ViewModel => (BundlePackagesEditorViewModel)DataContext;

    public BundlePackagesEditorView() { InitializeComponent(); }

    private void AddEntry_Click(object sender, RoutedEventArgs e) => ViewModel.AddEntry();

    private void Remove_Click(object sender, RoutedEventArgs e) => ViewModel.RemoveSelected();
}
