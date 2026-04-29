using System;

namespace FalkForge.Compiler.Msi.UI.Layout;

/// <summary>
/// Composes a declarative <see cref="DialogContent"/> against a <see cref="DialogLayout"/>
/// to produce a concrete <see cref="MsiDialogModel"/>.
/// </summary>
/// <remarks>
/// This is the phase-4 skeleton: the returned model carries the dialog name, canvas size
/// from the layout, and the title key, but holds no controls. Region policy resolution,
/// control placement, and customization are added in phases 5+.
/// <see cref="MsiDialogModel"/> is internal so this composer is internal as well; the public
/// surface for end-users will land in phase 7+ via fluent builders.
/// </remarks>
internal static class DialogComposer
{
    /// <summary>
    /// Compose a declarative <see cref="DialogContent"/> against a <paramref name="layout"/>
    /// to produce a concrete <see cref="MsiDialogModel"/>.
    /// </summary>
    public static MsiDialogModel Compose(DialogContent content, DialogLayout layout)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(layout);

        return new MsiDialogModel
        {
            Name = content.Name,
            Width = layout.CanvasWidth,
            Height = layout.CanvasHeight,
            Title = content.TitleLocKey ?? string.Empty,
            FirstControl = content.FirstControl ?? string.Empty,
            DefaultControl = content.DefaultControl,
            CancelControl = content.CancelControl,
        };
    }
}
