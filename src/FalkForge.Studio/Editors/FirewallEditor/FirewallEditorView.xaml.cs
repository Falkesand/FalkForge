using System.Windows;
using System.Windows.Controls;

namespace FalkForge.Studio.Editors.FirewallEditor;

public partial class FirewallEditorView : UserControl
{
    private FirewallEditorViewModel ViewModel => (FirewallEditorViewModel)DataContext;

    public FirewallEditorView() { InitializeComponent(); }

    private void AddEntry_Click(object sender, RoutedEventArgs e) => ViewModel.AddEntry();
    private void Remove_Click(object sender, RoutedEventArgs e) => ViewModel.RemoveSelected();
}
