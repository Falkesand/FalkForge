using System.Windows;
using System.Windows.Controls;

namespace FalkForge.Studio.Editors.ServicesEditor;

public partial class ServicesEditorView : UserControl
{
    private ServicesEditorViewModel ViewModel => (ServicesEditorViewModel)DataContext;

    public ServicesEditorView() { InitializeComponent(); }

    private void AddEntry_Click(object sender, RoutedEventArgs e) => ViewModel.AddEntry();

    private void Remove_Click(object sender, RoutedEventArgs e) => ViewModel.RemoveSelected();
}
