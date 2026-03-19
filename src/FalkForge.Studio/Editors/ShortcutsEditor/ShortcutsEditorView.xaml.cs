using System.Windows;
using System.Windows.Controls;

namespace FalkForge.Studio.Editors.ShortcutsEditor;

public partial class ShortcutsEditorView : UserControl
{
    private ShortcutsEditorViewModel ViewModel => (ShortcutsEditorViewModel)DataContext;

    public ShortcutsEditorView() { InitializeComponent(); }

    private void AddEntry_Click(object sender, RoutedEventArgs e) => ViewModel.AddEntry();

    private void Remove_Click(object sender, RoutedEventArgs e) => ViewModel.RemoveSelected();
}
