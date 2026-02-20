using System.Windows;
using FalkForge.Ui;

namespace MAS.Pages;

public abstract class MasPageBase<TView> : InstallerPage<TView>
    where TView : FrameworkElement, new()
{
    public virtual string? Subtitle => null;
    public virtual string NextButtonText => "Next";
    public virtual bool ShowPrintButton => false;
    public virtual bool ShowPreviousButton => true;
}
