using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Tables;
using FalkForge.Compiler.Msi.UI.Templates;
using FalkForge.Localization;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.UI;

[SupportedOSPlatform("windows")]
internal sealed class DialogEmitter
{
    private readonly MsiDatabase _database;

    internal DialogEmitter(MsiDatabase database)
    {
        _database = database;
    }

    internal Result<Unit> EmitDialogTables(MsiDialogSet dialogSet, PackageModel package)
    {
        if (dialogSet == MsiDialogSet.None)
            return Unit.Value;

        var template = GetTemplate(dialogSet);
        var dialogs = template.GetDialogs(package);

        // Resolve !(loc.X) references in dialog control text
        var resolverResult = BuildStringResolver(package);
        if (resolverResult.IsFailure)
            return Result<Unit>.Failure(resolverResult.Error);

        var resolver = resolverResult.Value;
        if (resolver is not null)
        {
            var resolveResult = ResolveDialogStrings(dialogs, resolver);
            if (resolveResult.IsFailure)
                return resolveResult;
        }

        var createResult = CreateUiTables();
        if (createResult.IsFailure)
            return createResult;

        var textStyleResult = EmitTextStyles();
        if (textStyleResult.IsFailure)
            return textStyleResult;

        var uiTextResult = EmitUIText();
        if (uiTextResult.IsFailure)
            return uiTextResult;

        foreach (var dialog in dialogs)
        {
            var result = EmitDialog(dialog);
            if (result.IsFailure)
                return result;
        }

        // Emit InstallUISequence entries for dialog flow
        var seqResult = EmitInstallUISequence(dialogs);
        if (seqResult.IsFailure)
            return seqResult;

        return Unit.Value;
    }

    private static IDialogTemplate GetTemplate(MsiDialogSet dialogSet)
    {
        return dialogSet switch
        {
            MsiDialogSet.Minimal => new MinimalDialogTemplate(),
            MsiDialogSet.InstallDir => new InstallDirDialogTemplate(),
            MsiDialogSet.FeatureTree => new FeatureTreeDialogTemplate(),
            MsiDialogSet.Mondo => new MondoDialogTemplate(),
            MsiDialogSet.Advanced => new AdvancedDialogTemplate(),
            _ => new MinimalDialogTemplate()
        };
    }

    private Result<Unit> CreateUiTables()
    {
        var tableStatements = new[]
        {
            MsiTableDefinitions.CreateDialogTable,
            MsiTableDefinitions.CreateControlTable,
            MsiTableDefinitions.CreateControlEventTable,
            MsiTableDefinitions.CreateControlConditionTable,
            MsiTableDefinitions.CreateEventMappingTable,
            MsiTableDefinitions.CreateTextStyleTable,
            MsiTableDefinitions.CreateUITextTable
        };

        foreach (var sql in tableStatements)
        {
            var result = _database.Execute(sql);
            if (result.IsFailure)
                return result;
        }

        return Unit.Value;
    }

    private Result<Unit> EmitTextStyles()
    {
        var styles = new (string Name, string FaceName, int Size, int? Color, int StyleBits)[]
        {
            ("DlgFont8", "Tahoma", 8, null, 0),
            ("DlgFontBold8", "Tahoma", 8, null, 1), // Bold
            ("DlgFont12", "Tahoma", 12, null, 0),
            ("DlgFontBold12", "Tahoma", 12, null, 1),
            ("VerdanaBold13", "Verdana", 13, null, 1)
        };

        foreach (var (name, faceName, size, color, styleBits) in styles)
        {
            var result = _database.InsertRow(
                "SELECT `TextStyle`, `FaceName`, `Size`, `Color`, `StyleBits` FROM `TextStyle`",
                record => record
                    .SetString(1, name)
                    .SetString(2, faceName)
                    .SetInteger(3, size)
                    .SetInteger(4, color ?? 0)
                    .SetInteger(5, styleBits));
            if (result.IsFailure)
                return result;
        }

        return Unit.Value;
    }

    private Result<Unit> EmitUIText()
    {
        var entries = new (string Key, string Text)[]
        {
            ("AbsentPath", ""),
            ("bytes", "bytes"),
            ("GB", "GB"),
            ("KB", "KB"),
            ("MB", "MB"),
            ("MenuAbsent", "Entire feature will be unavailable."),
            ("MenuAllLocal", "Will be installed on local hard drive."),
            ("MenuLocal", "Will be installed on local hard drive."),
            ("NewFolder", "New Folder|"),
            ("SelAbsentAbsent", "This feature will remain uninstalled."),
            ("SelChildCostNeg", "This feature frees [1] on your hard drive."),
            ("SelChildCostPos", "This feature requires [1] on your hard drive."),
            ("SelCostPending", "Compiling cost for this feature..."),
            ("SelParentCostNegNeg",
                "This feature frees [1] on your hard drive. It has [2] of [3] subfeatures selected. The subfeatures free [4] on your hard drive."),
            ("SelParentCostNegPos",
                "This feature frees [1] on your hard drive. It has [2] of [3] subfeatures selected. The subfeatures require [4] on your hard drive."),
            ("SelParentCostPosNeg",
                "This feature requires [1] on your hard drive. It has [2] of [3] subfeatures selected. The subfeatures free [4] on your hard drive."),
            ("SelParentCostPosPos",
                "This feature requires [1] on your hard drive. It has [2] of [3] subfeatures selected. The subfeatures require [4] on your hard drive."),
            ("TimeRemaining", "Time remaining: {[1] minutes }{[2] seconds}"),
            ("VolumeCostAvailable", "Available"),
            ("VolumeCostDifference", "Difference"),
            ("VolumeCostRequired", "Required"),
            ("VolumeCostSize", "Disk Size"),
            ("VolumeCostVolume", "Volume")
        };

        foreach (var (key, text) in entries)
        {
            var result = _database.InsertRow(
                "SELECT `Key`, `Text` FROM `UIText`",
                record => record
                    .SetString(1, key)
                    .SetString(2, text));
            if (result.IsFailure)
                return result;
        }

        return Unit.Value;
    }

    private Result<Unit> EmitDialog(MsiDialogModel dialog)
    {
        // Dialog table row
        var result = _database.InsertRow(
            "SELECT `Dialog`, `HCentering`, `VCentering`, `Width`, `Height`, `Attributes`, `Title`, `Control_First`, `Control_Default`, `Control_Cancel` FROM `Dialog`",
            record => record
                .SetString(1, dialog.Name)
                .SetInteger(2, dialog.HCentering)
                .SetInteger(3, dialog.VCentering)
                .SetInteger(4, dialog.Width)
                .SetInteger(5, dialog.Height)
                .SetInteger(6, (int)dialog.Attributes)
                .SetString(7, dialog.Title)
                .SetString(8, dialog.FirstControl)
                .SetString(9, dialog.DefaultControl)
                .SetString(10, dialog.CancelControl));
        if (result.IsFailure)
            return result;

        // Control table rows
        foreach (var control in dialog.Controls)
        {
            result = _database.InsertRow(
                "SELECT `Dialog_`, `Control`, `Type`, `X`, `Y`, `Width`, `Height`, `Attributes`, `Property`, `Text`, `Control_Next`, `Help` FROM `Control`",
                record => record
                    .SetString(1, dialog.Name)
                    .SetString(2, control.Name)
                    .SetString(3, control.Type.ToString())
                    .SetInteger(4, control.X)
                    .SetInteger(5, control.Y)
                    .SetInteger(6, control.Width)
                    .SetInteger(7, control.Height)
                    .SetInteger(8, (int)control.Attributes)
                    .SetString(9, control.Property)
                    .SetString(10, control.Text)
                    .SetString(11, control.NextControl)
                    .SetString(12, null));
            if (result.IsFailure)
                return result;
        }

        // ControlEvent table rows
        foreach (var evt in dialog.Events)
        {
            result = _database.InsertRow(
                "SELECT `Dialog_`, `Control_`, `Event`, `Argument`, `Condition`, `Ordering` FROM `ControlEvent`",
                record => record
                    .SetString(1, evt.DialogName)
                    .SetString(2, evt.ControlName)
                    .SetString(3, evt.Event.Value)
                    .SetString(4, evt.Argument)
                    .SetString(5, evt.Condition ?? "1")
                    .SetInteger(6, evt.Ordering));
            if (result.IsFailure)
                return result;
        }

        // ControlCondition table rows
        foreach (var cond in dialog.Conditions)
        {
            result = _database.InsertRow(
                "SELECT `Dialog_`, `Control_`, `Action`, `Condition` FROM `ControlCondition`",
                record => record
                    .SetString(1, cond.DialogName)
                    .SetString(2, cond.ControlName)
                    .SetString(3, cond.Action.ToString())
                    .SetString(4, cond.Condition));
            if (result.IsFailure)
                return result;
        }

        // EventMapping table rows
        foreach (var mapping in dialog.EventMappings)
        {
            result = _database.InsertRow(
                "SELECT `Dialog_`, `Control_`, `Event`, `Attribute` FROM `EventMapping`",
                record => record
                    .SetString(1, mapping.DialogName)
                    .SetString(2, mapping.ControlName)
                    .SetString(3, mapping.Event)
                    .SetString(4, mapping.Attribute));
            if (result.IsFailure)
                return result;
        }

        return Unit.Value;
    }

    private Result<Unit> EmitInstallUISequence(IReadOnlyList<MsiDialogModel> dialogs)
    {
        // Standard UI sequence actions for dialog-based installs
        var uiActions = new (string Action, string? Condition, int Sequence)[]
        {
            ("AppSearch", null, 50),
            ("LaunchConditions", null, 100),
            ("ValidateProductID", null, 700),
            ("CostInitialize", null, 800),
            ("FileCost", null, 900),
            ("CostFinalize", null, 1000),
            ("ExecuteAction", null, 1300)
        };

        foreach (var (action, condition, sequence) in uiActions)
        {
            var result = _database.InsertRow(
                "SELECT `Action`, `Condition`, `Sequence` FROM `InstallUISequence`",
                record => record
                    .SetString(1, action)
                    .SetString(2, condition ?? "")
                    .SetInteger(3, sequence));
            if (result.IsFailure)
                return result;
        }

        // Add specific dialogs to the sequence:
        // 1. First wizard dialog (entry point) — users navigate by sequence (EndDialog Return) or NewDialog
        // 2. ProgressDlg (at 1200, before ExecuteAction at 1300) — shows install progress
        // 3. ExitDlg (at 1310, after ExecuteAction completes)
        // Support dialogs (CancelDlg, BrowseDlg) are spawned, not sequenced.
        var supportDialogs = new HashSet<string> { DialogNames.Cancel, DialogNames.Browse };
        var firstDialog = dialogs.FirstOrDefault(d =>
            !supportDialogs.Contains(d.Name) &&
            d.Name != DialogNames.Progress &&
            d.Name != DialogNames.Exit);
        var exitDialog = dialogs.FirstOrDefault(d => d.Name == DialogNames.Exit);

        if (firstDialog is not null)
        {
            var result = _database.InsertRow(
                "SELECT `Action`, `Condition`, `Sequence` FROM `InstallUISequence`",
                record => record
                    .SetString(1, firstDialog.Name)
                    .SetString(2, "")
                    .SetInteger(3, 1100));
            if (result.IsFailure)
                return result;
        }

        var progressDialog = dialogs.FirstOrDefault(d => d.Name == DialogNames.Progress);
        if (progressDialog is not null)
        {
            var result = _database.InsertRow(
                "SELECT `Action`, `Condition`, `Sequence` FROM `InstallUISequence`",
                record => record
                    .SetString(1, progressDialog.Name)
                    .SetString(2, "")
                    .SetInteger(3, 1200));
            if (result.IsFailure)
                return result;
        }

        if (exitDialog is not null)
        {
            // ExitDlg shows after ExecuteAction completes
            var result = _database.InsertRow(
                "SELECT `Action`, `Condition`, `Sequence` FROM `InstallUISequence`",
                record => record
                    .SetString(1, exitDialog.Name)
                    .SetString(2, "")
                    .SetInteger(3, 1310));
            if (result.IsFailure)
                return result;
        }

        return Unit.Value;
    }

    private static Result<LocalizedStringResolver?> BuildStringResolver(PackageModel package)
    {
        var locData = package.LocalizationData;

        if (locData.Count == 0)
        {
            // Auto-load built-in en-US as fallback so !(loc.X) references resolve
            var builder = new LocalizationBuilder();
            builder.AddBuiltInCultures();
            builder.DefaultCulture("en-US");
            var buildResult = builder.Build();
            if (buildResult.IsFailure)
                return Result<LocalizedStringResolver?>.Failure(buildResult.Error);

            var models = buildResult.Value.Select(m => new LocalizationModel
            {
                Culture = m.Culture,
                Strings = m.Strings
            });
            return new LocalizedStringResolver(models, "en-US");
        }

        // Convert PackageModel.LocalizationData to LocalizationModels
        var locModels = locData.Select(d => new LocalizationModel
        {
            Culture = d.Culture,
            Strings = d.Strings
        }).ToList();

        // Use the first culture as default (the builder already validated this)
        var defaultCulture = locModels[0].Culture;
        return new LocalizedStringResolver(locModels, defaultCulture);
    }

    private static Result<Unit> ResolveDialogStrings(
        IReadOnlyList<MsiDialogModel> dialogs,
        LocalizedStringResolver resolver)
    {
        foreach (var dialog in dialogs)
        foreach (var control in dialog.Controls)
            if (control.Text is not null && control.Text.Contains("!(loc."))
            {
                var resolveResult = resolver.Resolve(control.Text);
                if (resolveResult.IsFailure)
                    return Result<Unit>.Failure(resolveResult.Error);
                control.Text = resolveResult.Value;
            }

        return Unit.Value;
    }
}