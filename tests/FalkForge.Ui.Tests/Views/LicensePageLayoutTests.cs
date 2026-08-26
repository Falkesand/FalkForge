namespace FalkForge.Ui.Tests.Views;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FalkForge.Ui.Tests.ViewModels;
using FalkForge.Ui.ViewModels;
using FalkForge.Ui.Views;
using Xunit;

/// <summary>
/// The licence page has to fit inside the wizard window, because the accept checkbox on it is the
/// only way past the licence.
/// <para>
/// It did not fit. The page put a fixed-height 210px text box and the checkbox in a StackPanel
/// whose desired height came to more than the page's slot, so WPF's layout clip cut the checkbox
/// away entirely. It stayed in the automation tree with a sensible rectangle, which is why
/// scripted runs sailed past it, but a screenshot of the running installer showed empty space
/// where it should have been and a person had nothing to click.
/// </para>
/// <para>
/// The 484 x 328 slot below is measured from the running installer: the page host inside the
/// 500 x 420 wizard window, after the 72px banner and the 52px button bar have taken their share.
/// </para>
/// </summary>
public class LicensePageLayoutTests
{
    private const double PageWidth = 484;
    private const double PageHeight = 328;

    [WpfFact]
    public void TheAcceptCheckboxIsFullyVisibleInTheWizardWindow()
    {
        var shell = new DefaultShellViewModel(new TestInstallerEngine());
        var page = new LicensePage
        {
            DataContext = shell.Pages.OfType<LicensePageViewModel>().Single()
        };

        var host = new Border { Child = page };
        var slot = new Size(PageWidth, PageHeight);
        host.Measure(slot);
        host.Arrange(new Rect(new Point(0, 0), slot));
        host.UpdateLayout();

        var checkbox = FindDescendant<CheckBox>(page);
        Assert.NotNull(checkbox);

        var visible = VisibleArea(checkbox, host);

        Assert.False(visible.IsEmpty, "the accept checkbox is clipped away completely");
        Assert.True(
            visible.Height >= checkbox.ActualHeight - 0.5,
            $"the accept checkbox is {checkbox.ActualHeight:0.##} tall but only {visible.Height:0.##} of it survives layout clipping");
        Assert.True(
            visible.Width >= checkbox.ActualWidth - 0.5,
            $"the accept checkbox is {checkbox.ActualWidth:0.##} wide but only {visible.Width:0.##} of it survives layout clipping");
    }

    /// <summary>
    /// The part of <paramref name="element"/> that survives every layout clip between it and
    /// <paramref name="root"/>, in <paramref name="root"/>'s coordinates. WPF clips a child that
    /// asked for more room than its parent arranged for it, which is what hides an element that
    /// still reports a plausible rectangle to automation.
    /// </summary>
    private static Rect VisibleArea(FrameworkElement element, Visual root)
    {
        var rect = new Rect(element.RenderSize);
        Visual current = element;

        while (!ReferenceEquals(current, root))
        {
            if (current is FrameworkElement fe && LayoutInformation.GetLayoutClip(fe) is { } clip)
            {
                rect = Rect.Intersect(rect, clip.Bounds);
                if (rect.IsEmpty)
                    return rect;
            }

            if (VisualTreeHelper.GetParent(current) is not Visual parent)
                break;

            rect = current.TransformToAncestor(parent).TransformBounds(rect);
            current = parent;
        }

        return rect;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                return match;

            if (FindDescendant<T>(child) is { } deeper)
                return deeper;
        }

        return null;
    }
}
