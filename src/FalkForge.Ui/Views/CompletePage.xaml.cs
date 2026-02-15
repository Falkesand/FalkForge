namespace FalkForge.Ui.Views;

using System.Windows;
using System.Windows.Controls;

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
