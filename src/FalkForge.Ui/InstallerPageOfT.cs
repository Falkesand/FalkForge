using System.Windows;

namespace FalkForge.Ui;

public abstract class InstallerPage<TView> : InstallerPage
    where TView : FrameworkElement, new()
{
    internal sealed override FrameworkElement CreateViewInternal()
    {
        var view = new TView();
        view.DataContext = this;
        return view;
    }
}