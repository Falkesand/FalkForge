using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using FalkForge.Models;

namespace FalkForge.Validation;

/// <summary>
/// Built-in rules for authored custom dialogs (<see cref="CustomDialogModel"/>, DLG010–DLG019).
/// These fail loud at validation time so an invalid dialog never becomes a silent drop or an
/// opaque MSI foreign-key failure at emit time.
/// </summary>
public static partial class CustomDialogRules
{
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.]*$")]
    private static partial Regex IdentifierRegex();

    // Control types that carry data and therefore require a bound MSI property.
    // (VolumeCostList / ProgressBar are intentionally excluded — their Control table
    // Property column is not used.)
    private static readonly FrozenSet<CustomControlType> PropertyBoundTypes =
        FrozenSet.Create(
            CustomControlType.Edit,
            CustomControlType.PathEdit,
            CustomControlType.CheckBox,
            CustomControlType.MaskedEdit,
            CustomControlType.RadioButtonGroup,
            CustomControlType.ComboBox,
            CustomControlType.ListBox,
            CustomControlType.DirectoryCombo,
            CustomControlType.DirectoryList,
            CustomControlType.SelectionTree);

    private static ModelPath DialogPath(int i) =>
        ModelPath.Root.Field("CustomDialogs").Index(i);

    /// <summary>DLG010 — Dialog Id is required.</summary>
    public static readonly ValidationRule Dlg010_IdRequired = new(
        new RuleId("DLG010"),
        Severity.Error,
        ModelSection.CustomDialog,
        "Custom dialog Id required",
        "Every custom dialog must have a non-empty Id.",
        static ctx =>
        {
            var v = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.CustomDialogs.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(ctx.Package.CustomDialogs[i].Id))
                    v.Add(new Violation(new RuleId("DLG010"), Severity.Error,
                        DialogPath(i).Field("Id"),
                        "Custom dialog Id is required."));
            }
            return v.ToImmutable();
        });

    /// <summary>DLG011 — Dialog Id must be a valid MSI identifier.</summary>
    public static readonly ValidationRule Dlg011_IdFormat = new(
        new RuleId("DLG011"),
        Severity.Error,
        ModelSection.CustomDialog,
        "Custom dialog Id format invalid",
        "MSI dialog identifiers must start with a letter or underscore and contain only letters, digits, underscores, and periods.",
        static ctx =>
        {
            var v = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.CustomDialogs.Count; i++)
            {
                var id = ctx.Package.CustomDialogs[i].Id;
                if (!string.IsNullOrWhiteSpace(id) && !IdentifierRegex().IsMatch(id))
                    v.Add(new Violation(new RuleId("DLG011"), Severity.Error,
                        DialogPath(i).Field("Id"),
                        $"Custom dialog Id '{id}' is not a valid MSI identifier."));
            }
            return v.ToImmutable();
        });

    /// <summary>DLG012 — Dialog Ids must be unique across the package.</summary>
    public static readonly ValidationRule Dlg012_IdUnique = new(
        new RuleId("DLG012"),
        Severity.Error,
        ModelSection.CustomDialog,
        "Custom dialog Id must be unique",
        "Two custom dialogs cannot share the same Id.",
        static ctx =>
        {
            var v = ImmutableArray.CreateBuilder<Violation>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < ctx.Package.CustomDialogs.Count; i++)
            {
                var id = ctx.Package.CustomDialogs[i].Id;
                if (!string.IsNullOrWhiteSpace(id) && !seen.Add(id))
                    v.Add(new Violation(new RuleId("DLG012"), Severity.Error,
                        DialogPath(i).Field("Id"),
                        $"Duplicate custom dialog Id '{id}'."));
            }
            return v.ToImmutable();
        });

    /// <summary>DLG013 — A dialog must have at least one control.</summary>
    public static readonly ValidationRule Dlg013_ControlsRequired = new(
        new RuleId("DLG013"),
        Severity.Error,
        ModelSection.CustomDialog,
        "Custom dialog requires at least one control",
        "A dialog with no controls has no Control_First and cannot be shown.",
        static ctx =>
        {
            var v = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.CustomDialogs.Count; i++)
            {
                if (ctx.Package.CustomDialogs[i].Controls.Count == 0)
                    v.Add(new Violation(new RuleId("DLG013"), Severity.Error,
                        DialogPath(i).Field("Controls"),
                        $"Custom dialog '{ctx.Package.CustomDialogs[i].Id}' must have at least one control."));
            }
            return v.ToImmutable();
        });

    /// <summary>DLG014 — Control Name is required.</summary>
    public static readonly ValidationRule Dlg014_ControlNameRequired = new(
        new RuleId("DLG014"),
        Severity.Error,
        ModelSection.CustomDialog,
        "Custom dialog control Name required",
        "Every control on a custom dialog must have a non-empty Name.",
        static ctx =>
        {
            var v = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.CustomDialogs.Count; i++)
            {
                var d = ctx.Package.CustomDialogs[i];
                for (var j = 0; j < d.Controls.Count; j++)
                {
                    if (string.IsNullOrWhiteSpace(d.Controls[j].Name))
                        v.Add(new Violation(new RuleId("DLG014"), Severity.Error,
                            DialogPath(i).Field("Controls").Index(j).Field("Name"),
                            $"Custom dialog '{d.Id}' has a control with no name."));
                }
            }
            return v.ToImmutable();
        });

    /// <summary>DLG015 — Control Name must be a valid MSI identifier.</summary>
    public static readonly ValidationRule Dlg015_ControlNameFormat = new(
        new RuleId("DLG015"),
        Severity.Error,
        ModelSection.CustomDialog,
        "Custom dialog control Name format invalid",
        "MSI control identifiers must start with a letter or underscore and contain only letters, digits, underscores, and periods.",
        static ctx =>
        {
            var v = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.CustomDialogs.Count; i++)
            {
                var d = ctx.Package.CustomDialogs[i];
                for (var j = 0; j < d.Controls.Count; j++)
                {
                    var name = d.Controls[j].Name;
                    if (!string.IsNullOrWhiteSpace(name) && !IdentifierRegex().IsMatch(name))
                        v.Add(new Violation(new RuleId("DLG015"), Severity.Error,
                            DialogPath(i).Field("Controls").Index(j).Field("Name"),
                            $"Custom dialog '{d.Id}' control '{name}' is not a valid MSI identifier."));
                }
            }
            return v.ToImmutable();
        });

    /// <summary>DLG016 — Control names must be unique within a dialog.</summary>
    public static readonly ValidationRule Dlg016_ControlNameUnique = new(
        new RuleId("DLG016"),
        Severity.Error,
        ModelSection.CustomDialog,
        "Custom dialog control names must be unique",
        "Two controls on the same dialog cannot share a Name.",
        static ctx =>
        {
            var v = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.CustomDialogs.Count; i++)
            {
                var d = ctx.Package.CustomDialogs[i];
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (var j = 0; j < d.Controls.Count; j++)
                {
                    var name = d.Controls[j].Name;
                    if (!string.IsNullOrWhiteSpace(name) && !seen.Add(name))
                        v.Add(new Violation(new RuleId("DLG016"), Severity.Error,
                            DialogPath(i).Field("Controls").Index(j).Field("Name"),
                            $"Custom dialog '{d.Id}' has duplicate control name '{name}'."));
                }
            }
            return v.ToImmutable();
        });

    /// <summary>DLG017 — Control_Next must reference a control in the same dialog (no dangling tab link).</summary>
    public static readonly ValidationRule Dlg017_NextControlExists = new(
        new RuleId("DLG017"),
        Severity.Error,
        ModelSection.CustomDialog,
        "Custom dialog Control_Next references unknown control",
        "A control's Next (tab order) target must be another control on the same dialog.",
        static ctx =>
        {
            var v = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.CustomDialogs.Count; i++)
            {
                var d = ctx.Package.CustomDialogs[i];
                var names = new HashSet<string>(StringComparer.Ordinal);
                for (var j = 0; j < d.Controls.Count; j++)
                {
                    if (!string.IsNullOrWhiteSpace(d.Controls[j].Name))
                        names.Add(d.Controls[j].Name);
                }

                for (var j = 0; j < d.Controls.Count; j++)
                {
                    var next = d.Controls[j].NextControl;
                    if (!string.IsNullOrWhiteSpace(next) && !names.Contains(next))
                        v.Add(new Violation(new RuleId("DLG017"), Severity.Error,
                            DialogPath(i).Field("Controls").Index(j).Field("NextControl"),
                            $"Custom dialog '{d.Id}' control '{d.Controls[j].Name}' has Next='{next}' which is not a control on this dialog."));
                }
            }
            return v.ToImmutable();
        });

    /// <summary>DLG018 — Data-bound control types must be bound to an MSI property.</summary>
    public static readonly ValidationRule Dlg018_PropertyRequired = new(
        new RuleId("DLG018"),
        Severity.Error,
        ModelSection.CustomDialog,
        "Custom dialog control missing required property",
        "Edit, CheckBox, PathEdit and similar data-bound controls must reference an MSI property.",
        static ctx =>
        {
            var v = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.CustomDialogs.Count; i++)
            {
                var d = ctx.Package.CustomDialogs[i];
                for (var j = 0; j < d.Controls.Count; j++)
                {
                    var c = d.Controls[j];
                    if (PropertyBoundTypes.Contains(c.Type) && string.IsNullOrWhiteSpace(c.Property))
                        v.Add(new Violation(new RuleId("DLG018"), Severity.Error,
                            DialogPath(i).Field("Controls").Index(j).Field("Property"),
                            $"Custom dialog '{d.Id}' control '{c.Name}' of type {c.Type} must be bound to a property."));
                }
            }
            return v.ToImmutable();
        });

    /// <summary>DLG019 — FirstControl / DefaultControl / CancelControl must reference an existing control.</summary>
    public static readonly ValidationRule Dlg019_DialogControlRefsExist = new(
        new RuleId("DLG019"),
        Severity.Error,
        ModelSection.CustomDialog,
        "Custom dialog references unknown control",
        "A dialog's FirstControl, DefaultControl and CancelControl must name controls on the dialog.",
        static ctx =>
        {
            var v = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.CustomDialogs.Count; i++)
            {
                var d = ctx.Package.CustomDialogs[i];
                var names = new HashSet<string>(StringComparer.Ordinal);
                for (var j = 0; j < d.Controls.Count; j++)
                {
                    if (!string.IsNullOrWhiteSpace(d.Controls[j].Name))
                        names.Add(d.Controls[j].Name);
                }

                CheckRef(v, names, d.Id, d.FirstControl, "FirstControl", DialogPath(i).Field("FirstControl"));
                CheckRef(v, names, d.Id, d.DefaultControl, "DefaultControl", DialogPath(i).Field("DefaultControl"));
                CheckRef(v, names, d.Id, d.CancelControl, "CancelControl", DialogPath(i).Field("CancelControl"));
            }
            return v.ToImmutable();
        });

    // MSI event verbs whose Argument is a required target (dialog / action / exit code).
    private static readonly FrozenSet<string> ArgumentRequiredEvents =
        FrozenSet.Create(StringComparer.Ordinal,
            "NewDialog", "SpawnDialog", "DoAction", "EndDialog");

    /// <summary>DLG020 — A control event verb must be non-empty.</summary>
    public static readonly ValidationRule Dlg020_EventVerbRequired = new(
        new RuleId("DLG020"),
        Severity.Error,
        ModelSection.CustomDialog,
        "Custom dialog control event verb required",
        "Every control event must name an MSI event verb (NewDialog, EndDialog, DoAction, [Property], …).",
        static ctx =>
        {
            var v = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.CustomDialogs.Count; i++)
            {
                var d = ctx.Package.CustomDialogs[i];
                for (var j = 0; j < d.Controls.Count; j++)
                {
                    var c = d.Controls[j];
                    for (var e = 0; e < c.Events.Count; e++)
                    {
                        if (string.IsNullOrWhiteSpace(c.Events[e].Event))
                            v.Add(new Violation(new RuleId("DLG020"), Severity.Error,
                                DialogPath(i).Field("Controls").Index(j).Field("Events").Index(e).Field("Event"),
                                $"Custom dialog '{d.Id}' control '{c.Name}' has a control event with no event verb."));
                    }
                }
            }
            return v.ToImmutable();
        });

    /// <summary>DLG021 — NewDialog / SpawnDialog / DoAction / EndDialog events must carry a non-empty argument.</summary>
    public static readonly ValidationRule Dlg021_EventArgumentRequired = new(
        new RuleId("DLG021"),
        Severity.Error,
        ModelSection.CustomDialog,
        "Custom dialog control event missing required argument",
        "NewDialog and SpawnDialog need a target dialog, DoAction needs an action, and EndDialog needs an exit code.",
        static ctx =>
        {
            var v = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.CustomDialogs.Count; i++)
            {
                var d = ctx.Package.CustomDialogs[i];
                for (var j = 0; j < d.Controls.Count; j++)
                {
                    var c = d.Controls[j];
                    for (var e = 0; e < c.Events.Count; e++)
                    {
                        var ev = c.Events[e];
                        if (!string.IsNullOrWhiteSpace(ev.Event)
                            && ArgumentRequiredEvents.Contains(ev.Event)
                            && string.IsNullOrWhiteSpace(ev.Argument))
                            v.Add(new Violation(new RuleId("DLG021"), Severity.Error,
                                DialogPath(i).Field("Controls").Index(j).Field("Events").Index(e).Field("Argument"),
                                $"Custom dialog '{d.Id}' control '{c.Name}' has a '{ev.Event}' event with no argument."));
                    }
                }
            }
            return v.ToImmutable();
        });

    /// <summary>DLG022 — A control condition expression must be non-empty.</summary>
    public static readonly ValidationRule Dlg022_ConditionExpressionRequired = new(
        new RuleId("DLG022"),
        Severity.Error,
        ModelSection.CustomDialog,
        "Custom dialog control condition expression required",
        "A control condition must carry a non-empty MSI condition expression.",
        static ctx =>
        {
            var v = ImmutableArray.CreateBuilder<Violation>();
            for (var i = 0; i < ctx.Package.CustomDialogs.Count; i++)
            {
                var d = ctx.Package.CustomDialogs[i];
                for (var j = 0; j < d.Controls.Count; j++)
                {
                    var c = d.Controls[j];
                    for (var k = 0; k < c.Conditions.Count; k++)
                    {
                        if (string.IsNullOrWhiteSpace(c.Conditions[k].Condition))
                            v.Add(new Violation(new RuleId("DLG022"), Severity.Error,
                                DialogPath(i).Field("Controls").Index(j).Field("Conditions").Index(k).Field("Condition"),
                                $"Custom dialog '{d.Id}' control '{c.Name}' has a control condition with no expression."));
                    }
                }
            }
            return v.ToImmutable();
        });

    private static void CheckRef(
        ImmutableArray<Violation>.Builder v, HashSet<string> names,
        string dialogId, string? reference, string field, ModelPath path)
    {
        if (!string.IsNullOrWhiteSpace(reference) && !names.Contains(reference))
            v.Add(new Violation(new RuleId("DLG019"), Severity.Error, path,
                $"Custom dialog '{dialogId}' {field}='{reference}' is not a control on this dialog."));
    }

    /// <summary>All DLG custom-dialog rules in order.</summary>
    public static readonly ValidationRule[] All =
    [
        Dlg010_IdRequired,
        Dlg011_IdFormat,
        Dlg012_IdUnique,
        Dlg013_ControlsRequired,
        Dlg014_ControlNameRequired,
        Dlg015_ControlNameFormat,
        Dlg016_ControlNameUnique,
        Dlg017_NextControlExists,
        Dlg018_PropertyRequired,
        Dlg019_DialogControlRefsExist,
        Dlg020_EventVerbRequired,
        Dlg021_EventArgumentRequired,
        Dlg022_ConditionExpressionRequired,
    ];
}
