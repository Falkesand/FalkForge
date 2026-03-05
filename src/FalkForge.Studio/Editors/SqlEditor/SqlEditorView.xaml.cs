using System.Windows;
using System.Windows.Controls;

namespace FalkForge.Studio.Editors.SqlEditor;

public partial class SqlEditorView : UserControl
{
    private SqlEditorViewModel ViewModel => (SqlEditorViewModel)DataContext;

    public SqlEditorView() { InitializeComponent(); }

    private void AddDatabase_Click(object sender, RoutedEventArgs e) => ViewModel.AddDatabase();
    private void RemoveDatabase_Click(object sender, RoutedEventArgs e) => ViewModel.RemoveSelectedDatabase();
    private void AddScript_Click(object sender, RoutedEventArgs e) => ViewModel.AddScript();
    private void RemoveScript_Click(object sender, RoutedEventArgs e) => ViewModel.RemoveSelectedScript();
}
