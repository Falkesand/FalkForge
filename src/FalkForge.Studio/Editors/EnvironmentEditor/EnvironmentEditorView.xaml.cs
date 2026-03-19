using System.Windows;
using System.Windows.Controls;

namespace FalkForge.Studio.Editors.EnvironmentEditor;

public partial class EnvironmentEditorView : UserControl
{
    private EnvironmentEditorViewModel ViewModel => (EnvironmentEditorViewModel)DataContext;

    public EnvironmentEditorView() { InitializeComponent(); }

    private void AddEntry_Click(object sender, RoutedEventArgs e) => ViewModel.AddEntry();

    private void Remove_Click(object sender, RoutedEventArgs e) => ViewModel.RemoveSelected();
}
