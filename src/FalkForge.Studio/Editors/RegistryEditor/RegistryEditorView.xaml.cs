using System.Windows;
using System.Windows.Controls;

namespace FalkForge.Studio.Editors.RegistryEditor;

public partial class RegistryEditorView : UserControl
{
    private RegistryEditorViewModel ViewModel => (RegistryEditorViewModel)DataContext;

    public RegistryEditorView() { InitializeComponent(); }

    private void AddEntry_Click(object sender, RoutedEventArgs e) => ViewModel.AddEntry();

    private void Remove_Click(object sender, RoutedEventArgs e) => ViewModel.RemoveSelected();
}
