using System.Windows;
using System.Windows.Controls;

namespace FalkForge.Studio.Editors.CustomActionsEditor;

public partial class CustomActionsEditorView : UserControl
{
    private CustomActionsEditorViewModel ViewModel => (CustomActionsEditorViewModel)DataContext;

    public CustomActionsEditorView() { InitializeComponent(); }

    private void AddEntry_Click(object sender, RoutedEventArgs e) => ViewModel.AddEntry();

    private void Remove_Click(object sender, RoutedEventArgs e) => ViewModel.RemoveSelected();
}
