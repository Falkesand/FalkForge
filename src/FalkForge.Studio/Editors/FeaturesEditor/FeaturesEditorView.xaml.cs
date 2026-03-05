using System.Windows;
using System.Windows.Controls;

namespace FalkForge.Studio.Editors.FeaturesEditor;

public partial class FeaturesEditorView : UserControl
{
    private FeaturesEditorViewModel ViewModel => (FeaturesEditorViewModel)DataContext;

    public FeaturesEditorView() { InitializeComponent(); }

    private void AddFeature_Click(object sender, RoutedEventArgs e)
    {
        var id = $"Feature{ViewModel.Features.Count + 1}";
        ViewModel.AddFeature(id, $"Feature {ViewModel.Features.Count + 1}");
    }

    private void Remove_Click(object sender, RoutedEventArgs e) => ViewModel.RemoveSelected();
}
