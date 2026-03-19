using System.Windows;
using FalkForge.Ui;

namespace MAS.Pages;

/// <summary>
/// Base class for all MAS installer pages. Provides common shell integration properties
/// (subtitle, button text overrides, print/previous visibility) that the custom
/// <see cref="Shell.MasInstallerWindow"/> binds to.
/// </summary>
public abstract class MasPageBase<TView> : InstallerPage<TView>
    where TView : FrameworkElement, new()
{
    public virtual string? Subtitle => null;
    public virtual string NextButtonText => Localize("Shell.NextButton");
    public virtual string PreviousButtonText => Localize("Shell.PreviousButton");
    public virtual string CancelButtonText => Localize("Shell.CancelButton");
    public virtual string PrintButtonText => Localize("Shell.PrintButton");
    public virtual bool ShowPrintButton => false;
    public virtual bool ShowPreviousButton => true;

    // Used by the shell window for the cancel confirmation dialog
    public string CancelConfirmMessage => Localize("Shell.CancelConfirmMessage");
    public string CancelConfirmTitle => Localize("Shell.CancelConfirmTitle");
}