using System.Windows;
using System.Windows.Controls;

namespace FalkForge.Studio.Editors.XmlConfigEditor;

public partial class XmlConfigEditorView : UserControl
{
    private XmlConfigEditorViewModel ViewModel => (XmlConfigEditorViewModel)DataContext;

    public XmlConfigEditorView() { InitializeComponent(); }

    private void AddEntry_Click(object sender, RoutedEventArgs e) => ViewModel.AddEntry();
    private void Remove_Click(object sender, RoutedEventArgs e) => ViewModel.RemoveSelected();
}
