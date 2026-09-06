using System.Collections.Immutable;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Compiler.Msi.UI.Layout;
using FalkForge.Compiler.Msi.UI.Layout.Builders;
using FalkForge.Compiler.Msi.UI.Templates;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Multi-table producer that emits all MSI UI dialog tables for a given
/// <see cref="MsiDialogSet"/>. When the dialog set is <see cref="MsiDialogSet.None"/>
/// the producer returns an empty array — no UI tables are added to the recipe.
///
/// <para>
/// Tables emitted (for any active dialog set):
/// <c>Dialog</c>, <c>Control</c>, <c>ControlEvent</c>, <c>ControlCondition</c>,
/// <c>EventMapping</c>, <c>TextStyle</c>, <c>UIText</c> — always — plus <c>RadioButton</c> only
/// when a dialog actually authors a RadioButtonGroup row (currently only
/// <c>MsiRMFilesInUse</c>, gated on <see cref="FalkForge.Models.PackageModel.EnableRestartManager"/>).
/// </para>
///
/// <para>
/// The producer re-uses the existing <see cref="IDialogTemplate"/> infrastructure.
/// <see cref="IDialogTemplate.GetDialogs"/> returns pure <see cref="MsiDialogModel"/>
/// data — no live database handle is involved — so the producer simply maps
/// those models into <see cref="RecipeTable"/> instances. The legacy
/// <c>DialogEmitter</c> (deleted in Phase 9); this producer is its recipe-pipeline replacement.
/// </para>
///
/// <para>
/// Localization: <c>!(loc.X)</c> references in control text, dialog titles, and the fixed
/// UIText table entries are resolved via the built-in en-US strings (same fallback the legacy
/// <c>DialogEmitter</c> used) before the rows are frozen into immutable cells. Control text and
/// dialog titles are resolved in place on the mutable <see cref="MsiDialogModel"/> list returned
/// by the template; UIText entries are resolved into a fresh array (the fixed key/text source is
/// a shared static array and must never be mutated in place).
/// </para>
///
/// <para>
/// Thread-safety: not required — recipe build is single-threaded.
/// </para>
///
/// <para>
/// Split across partial-class files by responsibility: this file holds construction,
/// the fixed TextStyle/UIText data, and the <see cref="Produce"/> orchestration;
/// <c>DialogSetProducer.Rows.cs</c> builds the per-table <see cref="RecipeRow"/> lists;
/// <c>DialogSetProducer.Localization.cs</c> resolves <c>!(loc.X)</c> references;
/// <c>DialogSetProducer.License.cs</c> injects the license RTF;
/// <c>DialogSetProducer.ExtensionSteps.cs</c> emits extension-inserted dialog steps;
/// <c>DialogSetProducer.Schemas.cs</c> builds the table schemas.
/// </para>
/// </summary>
internal sealed partial class DialogSetProducer : IMultiTableProducer
{
    // Extension-contributed, MSI-capable dialog step builders, drained from the extension
    // registry by MsiAuthoring. When a DialogCustomization inserts one of these steps by name,
    // its Build output is emitted here — the real emission path for the InsertStep feature.
    private readonly IReadOnlyList<IMsiDialogStepBuilder> _extensionStepBuilders;

    // When set, !(loc.*) control text is resolved with this culture as the default instead of
    // the first configured LocalizationData culture. MsiAuthoring uses it to rebuild the UI
    // localized to each additional culture when generating per-culture MST language transforms.
    private readonly string? _defaultCultureOverride;

    /// <summary>Creates a producer with no extension-contributed dialog steps.</summary>
    public DialogSetProducer()
        : this([])
    {
    }

    /// <summary>Creates a producer that can emit the given extension-contributed dialog steps.</summary>
    public DialogSetProducer(IReadOnlyList<IMsiDialogStepBuilder> extensionStepBuilders)
        : this(extensionStepBuilders, null)
    {
    }

    /// <summary>
    /// Creates a producer that resolves <c>!(loc.*)</c> control text with
    /// <paramref name="defaultCultureOverride"/> as the default culture (falling back to the
    /// first configured culture when <see langword="null"/>).
    /// </summary>
    public DialogSetProducer(
        IReadOnlyList<IMsiDialogStepBuilder> extensionStepBuilders, string? defaultCultureOverride)
    {
        _extensionStepBuilders = extensionStepBuilders ?? [];
        _defaultCultureOverride = defaultCultureOverride;
    }

    // ── Fixed text style rows — identical to legacy DialogEmitter.EmitTextStyles ──────
    // Tuple: (Name, FaceName, Size, Color, StyleBits)
    private static readonly (string Name, string FaceName, int Size, int? Color, int StyleBits)[]
        TextStyles =
        [
            ("DlgFont8",      "Tahoma",  8,  null, 0),
            ("DlgFontBold8",  "Tahoma",  8,  null, 1),
            ("DlgFont12",     "Tahoma",  12, null, 0),
            ("DlgFontBold12", "Tahoma",  12, null, 1),
            ("VerdanaBold13", "Verdana", 13, null, 1),
        ];

    // ── Fixed UIText rows — mirrors legacy DialogEmitter.EmitUIText, with one intentional
    // deviation: MenuAllLocal (SelectionTree's "install this feature AND its subfeatures" menu
    // entry) used to read identically to MenuLocal ("install just this feature"), which is wrong
    // per the Windows Installer SelectionTree control contract — the two options must read
    // differently or the feature-picker context menu is meaningless.
    //
    // CROSS-FILE INIT ORDER WARNING: DialogSetProducer.Localization.cs's UiTextLocDefaults field
    // initializer calls BuildUiTextLocDefaults(), which reads this array. That field lives in a
    // DIFFERENT partial-class file, and C# does not guarantee static field initializer order
    // ACROSS partial-class files — only within a single file, and only by declaration order (see
    // the same-file hazard already documented in DialogSetProducer.Localization.cs, which caused
    // an NRE failing 107+ tests during development). This currently works only because of
    // Compile-item ordering; do not move this declaration without re-checking that ordering.
    private static readonly (string Key, string Text)[] UiTextEntries =
    [
        ("AbsentPath",             ""),
        ("bytes",                  "bytes"),
        ("GB",                     "GB"),
        ("KB",                     "KB"),
        ("MB",                     "MB"),
        ("MenuAbsent",             "Entire feature will be unavailable."),
        ("MenuAllLocal",           "Entire feature will be installed on local hard drive."),
        ("MenuLocal",              "Will be installed on local hard drive."),
        ("NewFolder",              "New Folder|"),
        ("SelAbsentAbsent",        "This feature will remain uninstalled."),
        ("SelChildCostNeg",        "This feature frees [1] on your hard drive."),
        ("SelChildCostPos",        "This feature requires [1] on your hard drive."),
        ("SelCostPending",         "Compiling cost for this feature..."),
        ("SelParentCostNegNeg",
            "This feature frees [1] on your hard drive. It has [2] of [3] subfeatures selected. The subfeatures free [4] on your hard drive."),
        ("SelParentCostNegPos",
            "This feature frees [1] on your hard drive. It has [2] of [3] subfeatures selected. The subfeatures require [4] on your hard drive."),
        ("SelParentCostPosNeg",
            "This feature requires [1] on your hard drive. It has [2] of [3] subfeatures selected. The subfeatures free [4] on your hard drive."),
        ("SelParentCostPosPos",
            "This feature requires [1] on your hard drive. It has [2] of [3] subfeatures selected. The subfeatures require [4] on your hard drive."),
        ("TimeRemaining",          "Time remaining: {[1] minutes }{[2] seconds}"),
        ("VolumeCostAvailable",    "Available"),
        ("VolumeCostDifference",   "Difference"),
        ("VolumeCostRequired",     "Required"),
        ("VolumeCostSize",         "Disk Size"),
        ("VolumeCostVolume",       "Volume"),
    ];

    // ── Schemas — built once at class init, immutable ─────────────────────────

    private static readonly TableSchema DialogSchema   = BuildDialogSchema();
    private static readonly TableSchema ControlSchema  = BuildControlSchema();
    private static readonly TableSchema ControlEventSchema     = BuildControlEventSchema();
    private static readonly TableSchema ControlConditionSchema = BuildControlConditionSchema();
    private static readonly TableSchema EventMappingSchema     = BuildEventMappingSchema();
    private static readonly TableSchema TextStyleSchema        = BuildTextStyleSchema();
    private static readonly TableSchema UITextSchema           = BuildUITextSchema();
    private static readonly TableSchema RadioButtonSchema       = BuildRadioButtonSchema();

    /// <inheritdoc/>
    public Result<ImmutableArray<RecipeTable>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        PackageModel package = context.Resolved.Package;
        MsiDialogSet dialogSet = package.DialogSet;

        // Multi-culture localization is now realized as per-culture MST language transforms,
        // generated by MsiAuthoring after the base MSI is committed (see MsiAuthoring Step 6.6 /
        // LanguageTransformGenerator). This producer resolves the base MSI with the default culture
        // (or _defaultCultureOverride when rebuilding a localized variant for the transform diff);
        // it no longer warns about "dropped" cultures because none are dropped.

        // Compose the dialog set: stock template dialogs first (when a stock set is active),
        // then author-defined custom dialogs translated into the same internal model, then any
        // extension-contributed dialog steps referenced by DialogCustomization.InsertStep. The
        // fixed TextStyle/UIText rows below are emitted whenever the composed set is non-empty.
        var dialogs = new List<MsiDialogModel>();
        if (dialogSet != MsiDialogSet.None)
        {
            IDialogTemplate template = GetTemplate(dialogSet);
            dialogs.AddRange(template.GetDialogs(package));

            // The MsiRMFilesInUse dialog is not part of the wizard flow — the installer creates it
            // directly from InstallValidate when Restart Manager reports files in use at Full UI.
            // It is therefore appended per-package (gated on the author's opt-in) rather than
            // declared by each of the five stock templates.
            if (package.EnableRestartManager)
            {
                dialogs.Add(DialogComposer.Compose(
                    MsiRMFilesInUseDlgBuilder.Build(),
                    Layouts.Standard370x270,
                    package.DialogCustomization));
            }
        }

        for (int cd = 0; cd < package.CustomDialogs.Count; cd++)
        {
            dialogs.Add(CustomDialogTranslator.Translate(package.CustomDialogs[cd]));
        }

        AppendInsertedExtensionStepDialogs(package, dialogs);

        // Author each composed dialog's Control_Next tab cycle here — the single point where
        // stock templates, Restart Manager's MsiRMFilesInUse, author-defined custom dialogs, and
        // extension-contributed steps have all converged into one list, so no dialog source can
        // forget to wire Control_Next. See DialogTabCycle's remarks for why this is not done
        // inside DialogComposer.Compose instead.
        for (int di = 0; di < dialogs.Count; di++)
        {
            DialogTabCycle.Assign(dialogs[di]);
        }

        // Nothing to emit → no UI tables (matches the legacy "no UI = no tables" behaviour).
        if (dialogs.Count == 0)
        {
            return Result<ImmutableArray<RecipeTable>>.Success(
                ImmutableArray<RecipeTable>.Empty);
        }

        // Every modal dialog the sequence schedules must be able to reach an EndDialog, or the
        // installer waits on it and the sequence never resumes. See CheckInstallFlow's remarks.
        Result<Unit> flowResult = CheckInstallFlow(dialogs, package);
        if (flowResult.IsFailure)
        {
            return Result<ImmutableArray<RecipeTable>>.Failure(flowResult.Error);
        }

        // Resolve !(loc.X) references in control text, dialog titles, and UIText entries,
        // mirroring DialogEmitter.BuildStringResolver (extended beyond Control.Text in beta.4).
        Result<ImmutableArray<(string Key, string Text)>> resolveResult =
            ResolveLocalizationRefs(dialogs, package, _defaultCultureOverride);
        if (resolveResult.IsFailure)
        {
            return Result<ImmutableArray<RecipeTable>>.Failure(resolveResult.Error);
        }

        // Inject the license RTF into the ScrollableText license control's Text column.
        // Runs after localization resolution so the raw RTF is never scanned for !(loc.X).
        Result<Unit> licenseResult = InjectLicenseText(dialogs, package, context);
        if (licenseResult.IsFailure)
        {
            return Result<ImmutableArray<RecipeTable>>.Failure(licenseResult.Error);
        }

        return Result<ImmutableArray<RecipeTable>>.Success(
            BuildDialogTables(dialogs, resolveResult.Value));
    }


    // Dialogs InstallUISequence schedules for a stock set, mirroring
    // InstallUISequenceTableProducer.GetDialogFlowRows. Progress is modeless and Exit ends the
    // sequence itself, so in practice Welcome is the root that matters, but listing all three
    // keeps this honest if the flow rows change.
    private static readonly string[] StockScheduledDialogs =
        [DialogNames.Welcome, DialogNames.Progress, DialogNames.Exit];

    /// <summary>
    /// Fails the build when a modal dialog that <c>InstallUISequence</c> schedules cannot reach an
    /// <c>EndDialog</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Windows Installer runs a modal dialog in <c>InstallUISequence</c> as a blocking message
    /// loop that ends only when a control publishes <c>EndDialog</c>. <c>NewDialog</c> ends the
    /// current dialog and opens the target inside that same wait, so a chain of NewDialog hops is
    /// still one wait; it ends only when some dialog in the chain publishes <c>EndDialog</c>. If
    /// none does, the sequence parks and <c>ExecuteAction</c> never runs, so nothing installs.
    /// </para>
    /// <para>
    /// The walk follows <c>NewDialog</c> edges ONLY. <c>SpawnDialog</c> opens a child on top and
    /// hands control back to the parent when the child closes, so an <c>EndDialog</c> inside a
    /// spawned child ends the child rather than resuming the sequence. Following SpawnDialog would
    /// accept a dialog whose only EndDialog sits in a cancel confirmation, which can abort the
    /// install but can never start it.
    /// </para>
    /// <para>
    /// Roots are the dialogs a stock set schedules, plus custom dialogs carrying a
    /// <c>SequenceNumber</c>, plus names added through <c>UISequence</c>. A dialog nothing
    /// navigates to and nothing schedules emits an orphan row, which is legal MSI and hangs
    /// nothing, so it is deliberately out of scope.
    /// </para>
    /// </remarks>
    private static Result<Unit> CheckInstallFlow(List<MsiDialogModel> dialogs, PackageModel package)
    {
        var byName = new Dictionary<string, MsiDialogModel>(StringComparer.Ordinal);
        for (int i = 0; i < dialogs.Count; i++)
        {
            byName[dialogs[i].Name] = dialogs[i];
        }

        var roots = new HashSet<string>(StockScheduledDialogs, StringComparer.Ordinal);
        for (int c = 0; c < package.CustomDialogs.Count; c++)
        {
            CustomDialogModel custom = package.CustomDialogs[c];
            if (custom.SequenceNumber is not null)
            {
                roots.Add(custom.Id);
            }
        }

        // Sequence(...) is not the only way into InstallUISequence. UISequence adds an action by
        // name and the sequence producer writes any name through verbatim, so a dialog scheduled
        // that way can park the sequence exactly as one scheduled by Sequence(...) can. Names that
        // are not dialogs never match a composed dialog and are ignored.
        for (int u = 0; u < package.UISequenceActions.Count; u++)
        {
            SequenceActionModel action = package.UISequenceActions[u];
            if (action.Table == SequenceTable.InstallUISequence)
            {
                roots.Add(action.ActionName);
            }
        }

        for (int i = 0; i < dialogs.Count; i++)
        {
            MsiDialogModel root = dialogs[i];
            if (!root.Attributes.HasFlag(MsiDialogAttributes.Modal) || !roots.Contains(root.Name))
            {
                continue;
            }

            if (!CanReachEndDialog(root, byName))
            {
                return Result<Unit>.Failure(
                    ErrorKind.Validation,
                    $"DLG024: dialog '{root.Name}' is scheduled in InstallUISequence and is modal, but no dialog "
                    + "reachable from it by NewDialog publishes EndDialog, so the installer waits on it and the "
                    + "sequence never resumes. Publish EndDialog with argument Return on the control that continues "
                    + "the install, or clear the Modal attribute bit (0x2) if the dialog is meant to be modeless. "
                    + "An EndDialog inside a dialog opened with SpawnDialog does not count, because control returns "
                    + "to the spawning dialog rather than to the sequence.");
            }
        }

        return Result<Unit>.Success(Unit.Value);
    }

    private static bool CanReachEndDialog(MsiDialogModel root, Dictionary<string, MsiDialogModel> byName)
    {
        // Author text reaches here verbatim: MsiControlEvent.Parse accepts any non-empty string
        // and normalises nothing, so trim and ignore case. Over-accepting the verb is the safe
        // direction for a check that fails the build.
        static bool IsVerb(MsiControlEventModel e, string verb) =>
            string.Equals(e.Event.Value.Trim(), verb, StringComparison.OrdinalIgnoreCase);

        var seen = new HashSet<string>(StringComparer.Ordinal) { root.Name };
        var pending = new Stack<MsiDialogModel>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            MsiDialogModel dialog = pending.Pop();
            for (int e = 0; e < dialog.Events.Count; e++)
            {
                MsiControlEventModel controlEvent = dialog.Events[e];
                // The argument matters as much as the verb. EndDialog Exit terminates the UI
                // without running the install, so a wizard whose only exit is Exit can be closed
                // but can never install. Only Return hands control back to InstallUISequence so it
                // can reach ExecuteAction, which is why DialogFooter.StartsInstall requires that
                // same argument.
                if (IsVerb(controlEvent, "EndDialog")
                    && string.Equals(controlEvent.Argument.Trim(), "Return", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // Unknown targets are skipped rather than followed. A dangling navigation target
                // is its own defect and is not what this check reports.
                if (IsVerb(controlEvent, "NewDialog")
                    && byName.TryGetValue(controlEvent.Argument, out MsiDialogModel? next)
                    && seen.Add(next.Name))
                {
                    pending.Push(next);
                }
            }
        }

        return false;
    }

    // ── Template selection (mirrors legacy DialogEmitter.GetTemplate) ────────────────

    private static IDialogTemplate GetTemplate(MsiDialogSet dialogSet)
        => dialogSet switch
        {
            MsiDialogSet.Minimal     => new MinimalDialogTemplate(),
            MsiDialogSet.InstallDir  => new InstallDirDialogTemplate(),
            MsiDialogSet.FeatureTree => new FeatureTreeDialogTemplate(),
            MsiDialogSet.Mondo       => new MondoDialogTemplate(),
            MsiDialogSet.Advanced    => new AdvancedDialogTemplate(),
            _                        => new MinimalDialogTemplate(),
        };
}
