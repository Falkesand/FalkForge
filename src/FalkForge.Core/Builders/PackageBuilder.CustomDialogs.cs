using System;
using System.Collections.Generic;

using FalkForge.Models;

namespace FalkForge.Builders;

// Custom MSI dialog authoring: define complete dialogs (controls, events, conditions,
// tab order) that the compiler emits into the MSI UI tables.
public sealed partial class PackageBuilder
{
    private readonly List<CustomDialogModel> _customDialogs = [];

    /// <summary>
    /// Authors a custom MSI dialog and adds it to the package. The dialog is emitted into the
    /// MSI <c>Dialog</c> / <c>Control</c> / <c>ControlEvent</c> / <c>ControlCondition</c> tables
    /// in addition to any active stock <see cref="MsiDialogSet"/>. Mark a dialog as an install-UI
    /// entry point with <see cref="CustomDialogBuilder.Sequence(int)"/>, or make it reachable from
    /// another dialog via a <c>NewDialog</c> / <c>SpawnDialog</c> event.
    /// </summary>
    /// <param name="id">The dialog identifier, unique across the package.</param>
    /// <param name="configure">Callback that authors the dialog through a <see cref="CustomDialogBuilder"/>.</param>
    public PackageBuilder AddCustomDialog(string id, Action<CustomDialogBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new CustomDialogBuilder(id);
        configure(builder);
        _customDialogs.Add(builder.Build());
        return this;
    }
}
