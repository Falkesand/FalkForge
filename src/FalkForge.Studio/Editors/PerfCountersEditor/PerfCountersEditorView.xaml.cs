using System.Windows;
using System.Windows.Controls;

namespace FalkForge.Studio.Editors.PerfCountersEditor;

public partial class PerfCountersEditorView : UserControl
{
    private PerfCountersEditorViewModel ViewModel => (PerfCountersEditorViewModel)DataContext;

    public PerfCountersEditorView() { InitializeComponent(); }

    private void AddEntry_Click(object sender, RoutedEventArgs e) => ViewModel.AddEntry();
    private void Remove_Click(object sender, RoutedEventArgs e) => ViewModel.RemoveSelected();
}
