using System.Windows;
using System.Windows.Controls;

namespace FalkForge.Studio.Editors.OdbcEditor;

public partial class OdbcEditorView : UserControl
{
    private OdbcEditorViewModel ViewModel => (OdbcEditorViewModel)DataContext;

    public OdbcEditorView() { InitializeComponent(); }

    private void AddDriver_Click(object sender, RoutedEventArgs e) => ViewModel.AddDriver();
    private void RemoveDriver_Click(object sender, RoutedEventArgs e) => ViewModel.RemoveSelectedDriver();
    private void AddDataSource_Click(object sender, RoutedEventArgs e) => ViewModel.AddDataSource();
    private void RemoveDataSource_Click(object sender, RoutedEventArgs e) => ViewModel.RemoveSelectedDataSource();
}
