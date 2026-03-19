using System.Windows;
using System.Windows.Controls;

namespace FalkForge.Ui.Views;

public partial class CompletePage : UserControl
{
    public CompletePage()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        window?.Close();
    }
}