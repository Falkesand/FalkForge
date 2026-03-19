using System.Windows;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Shell;

public partial class NewProjectDialog : Window
{
    public ProjectTemplate? SelectedTemplate { get; private set; }

    public NewProjectDialog()
    {
        InitializeComponent();
        TemplateList.ItemsSource = ProjectTemplates.All;
        TemplateList.SelectedIndex = 0;
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        SelectedTemplate = TemplateList.SelectedItem as ProjectTemplate;
        DialogResult = true;
    }
}
