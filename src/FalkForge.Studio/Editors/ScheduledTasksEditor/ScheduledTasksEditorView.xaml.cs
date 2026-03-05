using System.Windows;
using System.Windows.Controls;

namespace FalkForge.Studio.Editors.ScheduledTasksEditor;

public partial class ScheduledTasksEditorView : UserControl
{
    private ScheduledTasksEditorViewModel ViewModel => (ScheduledTasksEditorViewModel)DataContext;

    public ScheduledTasksEditorView() { InitializeComponent(); }

    private void AddEntry_Click(object sender, RoutedEventArgs e) => ViewModel.AddEntry();
    private void Remove_Click(object sender, RoutedEventArgs e) => ViewModel.RemoveSelected();
}
