using System.Windows.Controls;
using System.Windows.Input;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.DialogEditor;

public partial class DialogEditorView : UserControl
{
    public DialogEditorView() { InitializeComponent(); }

    private DialogEditorViewModel ViewModel => (DialogEditorViewModel)DataContext;

    private void Control_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DialogControlPresenter presenter && presenter.Control is DialogControlDefinition control)
        {
            ViewModel.SelectedControl = control;
            e.Handled = true;
        }
    }
}
