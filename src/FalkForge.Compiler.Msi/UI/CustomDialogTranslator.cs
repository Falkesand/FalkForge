using System;
using System.Collections.Generic;

using FalkForge.Models;

namespace FalkForge.Compiler.Msi.UI;

/// <summary>
/// Translates author-facing <see cref="CustomDialogModel"/> instances (from
/// <c>FalkForge.Core</c>) into the internal <see cref="MsiDialogModel"/> the recipe pipeline
/// emits. This is the real "build" path for the public custom-dialog authoring API: the raw
/// MSI dialog model stays internal, and this translator is the only bridge to it.
/// </summary>
internal static class CustomDialogTranslator
{
    /// <summary>Translates one authored dialog into an internal <see cref="MsiDialogModel"/>.</summary>
    public static MsiDialogModel Translate(CustomDialogModel dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        var model = new MsiDialogModel
        {
            Name = dialog.Id,
            Title = dialog.Title,
            Width = dialog.Width,
            Height = dialog.Height,
            HCentering = dialog.HCentering,
            VCentering = dialog.VCentering,
            Attributes = (MsiDialogAttributes)dialog.Attributes,
            FirstControl = ResolveFirstControl(dialog),
            DefaultControl = dialog.DefaultControl,
            CancelControl = dialog.CancelControl,
        };

        IReadOnlyList<CustomDialogControlModel> controls = dialog.Controls;
        for (int i = 0; i < controls.Count; i++)
        {
            CustomDialogControlModel c = controls[i];

            model.Controls.Add(new MsiControlModel
            {
                Name = c.Name,
                Type = MapControlType(c.Type),
                X = c.X,
                Y = c.Y,
                Width = c.Width,
                Height = c.Height,
                Attributes = (MsiControlAttributes)c.Attributes,
                Property = c.Property,
                Text = c.Text,
                NextControl = c.NextControl,
            });

            IReadOnlyList<CustomDialogControlEventModel> events = c.Events;
            for (int e = 0; e < events.Count; e++)
            {
                CustomDialogControlEventModel ev = events[e];
                model.Events.Add(new MsiControlEventModel
                {
                    DialogName = dialog.Id,
                    ControlName = c.Name,
                    Event = MsiControlEvent.Parse(ev.Event),
                    Argument = ev.Argument,
                    Condition = ev.Condition,
                    Ordering = ev.Ordering,
                });
            }

            IReadOnlyList<CustomDialogControlConditionModel> conditions = c.Conditions;
            for (int k = 0; k < conditions.Count; k++)
            {
                CustomDialogControlConditionModel cond = conditions[k];
                model.Conditions.Add(new MsiControlConditionModel
                {
                    DialogName = dialog.Id,
                    ControlName = c.Name,
                    Action = MapConditionAction(cond.Action),
                    Condition = cond.Condition,
                });
            }
        }

        return model;
    }

    private static string ResolveFirstControl(CustomDialogModel dialog)
    {
        if (!string.IsNullOrEmpty(dialog.FirstControl))
        {
            return dialog.FirstControl;
        }

        // Fall back to the first authored control so the Dialog.Control_First column is never
        // empty. Validation (DLG013) guarantees at least one control by the time a package is
        // compiled; the guard here keeps the translator total for any caller.
        return dialog.Controls.Count > 0 ? dialog.Controls[0].Name : string.Empty;
    }

    // The Core CustomControlType and the internal MsiControlType share member names by design,
    // but an explicit map avoids reflection and fails loud if the two ever drift.
    private static MsiControlType MapControlType(CustomControlType type) => type switch
    {
        CustomControlType.Text => MsiControlType.Text,
        CustomControlType.PushButton => MsiControlType.PushButton,
        CustomControlType.Line => MsiControlType.Line,
        CustomControlType.CheckBox => MsiControlType.CheckBox,
        CustomControlType.ScrollableText => MsiControlType.ScrollableText,
        CustomControlType.PathEdit => MsiControlType.PathEdit,
        CustomControlType.SelectionTree => MsiControlType.SelectionTree,
        CustomControlType.VolumeCostList => MsiControlType.VolumeCostList,
        CustomControlType.ProgressBar => MsiControlType.ProgressBar,
        CustomControlType.Bitmap => MsiControlType.Bitmap,
        CustomControlType.RadioButtonGroup => MsiControlType.RadioButtonGroup,
        CustomControlType.ComboBox => MsiControlType.ComboBox,
        CustomControlType.Edit => MsiControlType.Edit,
        CustomControlType.ListBox => MsiControlType.ListBox,
        CustomControlType.DirectoryCombo => MsiControlType.DirectoryCombo,
        CustomControlType.DirectoryList => MsiControlType.DirectoryList,
        CustomControlType.MaskedEdit => MsiControlType.MaskedEdit,
        CustomControlType.Icon => MsiControlType.Icon,
        CustomControlType.GroupBox => MsiControlType.GroupBox,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown custom control type."),
    };

    private static MsiConditionAction MapConditionAction(CustomConditionAction action) => action switch
    {
        CustomConditionAction.Default => MsiConditionAction.Default,
        CustomConditionAction.Disable => MsiConditionAction.Disable,
        CustomConditionAction.Enable => MsiConditionAction.Enable,
        CustomConditionAction.Hide => MsiConditionAction.Hide,
        CustomConditionAction.Show => MsiConditionAction.Show,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown custom condition action."),
    };
}
