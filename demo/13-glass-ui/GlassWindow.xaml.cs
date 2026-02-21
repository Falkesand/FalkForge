using System.Windows;
using System.Windows.Controls;

namespace GlassUi;

public partial class GlassWindow : Window
{
    public GlassWindow()
    {
        InitializeComponent();
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not (Button or TextBlock))
                DragMove();
        };
    }
}
