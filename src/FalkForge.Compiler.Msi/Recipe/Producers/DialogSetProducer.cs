using System.Collections.Immutable;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Compiler.Msi.UI.Layout;
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
/// <c>EventMapping</c>, <c>TextStyle</c>, <c>UIText</c>.
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
/// Localization: <c>!(loc.X)</c> references in control text are resolved via
/// the built-in en-US strings (same fallback the legacy <c>DialogEmitter</c> used)
/// before the rows are frozen into immutable cells. The resolver runs on the
/// mutable <see cref="MsiDialogModel"/> list returned by the template, so the
/// resolution is non-destructive to the original model objects.
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

    // ── Fixed UIText rows — identical to legacy DialogEmitter.EmitUIText ─────────────
    private static readonly (string Key, string Text)[] UiTextEntries =
    [
        ("AbsentPath",             ""),
        ("bytes",                  "bytes"),
        ("GB",                     "GB"),
        ("KB",                     "KB"),
        ("MB",                     "MB"),
        ("MenuAbsent",             "Entire feature will be unavailable."),
        ("MenuAllLocal",           "Will be installed on local hard drive."),
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
        }

        for (int cd = 0; cd < package.CustomDialogs.Count; cd++)
        {
            dialogs.Add(CustomDialogTranslator.Translate(package.CustomDialogs[cd]));
        }

        AppendInsertedExtensionStepDialogs(package, dialogs);

        // Nothing to emit → no UI tables (matches the legacy "no UI = no tables" behaviour).
        if (dialogs.Count == 0)
        {
            return Result<ImmutableArray<RecipeTable>>.Success(
                ImmutableArray<RecipeTable>.Empty);
        }

        // Resolve !(loc.X) references in control text, mirroring DialogEmitter.BuildStringResolver.
        Result<Unit> resolveResult = ResolveLocalizationRefs(dialogs, package, _defaultCultureOverride);
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

        return Result<ImmutableArray<RecipeTable>>.Success(BuildDialogTables(dialogs));
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
