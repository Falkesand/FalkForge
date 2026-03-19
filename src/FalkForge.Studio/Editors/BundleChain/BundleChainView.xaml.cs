using System.Windows;
using System.Windows.Controls;

namespace FalkForge.Studio.Editors.BundleChain;

public partial class BundleChainView : UserControl
{
    private BundleChainViewModel ViewModel => (BundleChainViewModel)DataContext;

    public BundleChainView() { InitializeComponent(); }

    private void AddRollbackBoundary_Click(object sender, RoutedEventArgs e) => ViewModel.AddRollbackBoundary();

    private void RemoveItem_Click(object sender, RoutedEventArgs e) => ViewModel.RemoveItem();
}
