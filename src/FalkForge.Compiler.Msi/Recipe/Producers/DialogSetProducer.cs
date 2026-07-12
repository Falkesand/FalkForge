using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using FalkForge.Compiler.Msi.Tables;
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
/// </summary>
internal sealed partial class DialogSetProducer : IMultiTableProducer
{
    // Extension-contributed, MSI-capable dialog step builders, drained from the extension
    // registry by MsiAuthoring. When a DialogCustomization inserts one of these steps by name,
    // its Build output is emitted here — the real emission path for the InsertStep feature.
    private readonly IReadOnlyList<IMsiDialogStepBuilder> _extensionStepBuilders;

    /// <summary>Creates a producer with no extension-contributed dialog steps.</summary>
    public DialogSetProducer()
        : this([])
    {
    }

    /// <summary>Creates a producer that can emit the given extension-contributed dialog steps.</summary>
    public DialogSetProducer(IReadOnlyList<IMsiDialogStepBuilder> extensionStepBuilders)
    {
        _extensionStepBuilders = extensionStepBuilders ?? [];
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
        Result<Unit> resolveResult = ResolveLocalizationRefs(dialogs, package);
        if (resolveResult.IsFailure)
        {
            return Result<ImmutableArray<RecipeTable>>.Failure(resolveResult.Error);
        }

        // Build per-table row lists by iterating dialogs once each.
        ImmutableArray<RecipeRow>.Builder dialogRows  = ImmutableArray.CreateBuilder<RecipeRow>();
        ImmutableArray<RecipeRow>.Builder controlRows = ImmutableArray.CreateBuilder<RecipeRow>();
        ImmutableArray<RecipeRow>.Builder ceRows      = ImmutableArray.CreateBuilder<RecipeRow>();
        ImmutableArray<RecipeRow>.Builder ccRows      = ImmutableArray.CreateBuilder<RecipeRow>();
        ImmutableArray<RecipeRow>.Builder emRows      = ImmutableArray.CreateBuilder<RecipeRow>();

        // Index-based loop avoids IReadOnlyList<T> enumerator heap allocation (HAA0401).
        for (int di = 0; di < dialogs.Count; di++)
        {
            MsiDialogModel d = dialogs[di];

            // Dialog row
            dialogRows.Add(new RecipeRow
            {
                Cells = ImmutableArray.Create<CellValue>(
                    new CellValue.StringValue(d.Name),
                    new CellValue.IntValue(d.HCentering),
                    new CellValue.IntValue(d.VCentering),
                    new CellValue.IntValue(d.Width),
                    new CellValue.IntValue(d.Height),
                    new CellValue.IntValue((int)d.Attributes),
                    StringOrNull(d.Title),
                    new CellValue.StringValue(d.FirstControl),
                    StringOrNull(d.DefaultControl),
                    StringOrNull(d.CancelControl)),
            });

            // Control rows
            for (int ci = 0; ci < d.Controls.Count; ci++)
            {
                MsiControlModel c = d.Controls[ci];
                controlRows.Add(new RecipeRow
                {
                    Cells = ImmutableArray.Create<CellValue>(
                        new CellValue.StringValue(d.Name),
                        new CellValue.StringValue(c.Name),
                        new CellValue.StringValue(c.Type.ToString()),
                        new CellValue.IntValue(c.X),
                        new CellValue.IntValue(c.Y),
                        new CellValue.IntValue(c.Width),
                        new CellValue.IntValue(c.Height),
                        new CellValue.IntValue((int)c.Attributes),
                        StringOrNull(c.Property),
                        StringOrNull(c.Text),
                        StringOrNull(c.NextControl),
                        new CellValue.Null()),   // Help — always null
                });
            }

            // ControlEvent rows
            for (int ei = 0; ei < d.Events.Count; ei++)
            {
                MsiControlEventModel e = d.Events[ei];
                ceRows.Add(new RecipeRow
                {
                    Cells = ImmutableArray.Create<CellValue>(
                        new CellValue.StringValue(e.DialogName),
                        new CellValue.StringValue(e.ControlName),
                        new CellValue.StringValue(e.Event.Value),
                        new CellValue.StringValue(e.Argument),
                        e.Condition is not null
                            ? new CellValue.StringValue(e.Condition)
                            : new CellValue.StringValue("1"),   // default condition matches DialogEmitter
                        new CellValue.IntValue(e.Ordering)),
                });
            }

            // ControlCondition rows
            for (int ki = 0; ki < d.Conditions.Count; ki++)
            {
                MsiControlConditionModel k = d.Conditions[ki];
                ccRows.Add(new RecipeRow
                {
                    Cells = ImmutableArray.Create<CellValue>(
                        new CellValue.StringValue(k.DialogName),
                        new CellValue.StringValue(k.ControlName),
                        new CellValue.StringValue(k.Action.ToString()),
                        new CellValue.StringValue(k.Condition)),
                });
            }

            // EventMapping rows
            for (int mi = 0; mi < d.EventMappings.Count; mi++)
            {
                MsiEventMappingModel m = d.EventMappings[mi];
                emRows.Add(new RecipeRow
                {
                    Cells = ImmutableArray.Create<CellValue>(
                        new CellValue.StringValue(m.DialogName),
                        new CellValue.StringValue(m.ControlName),
                        new CellValue.StringValue(m.Event),
                        new CellValue.StringValue(m.Attribute)),
                });
            }
        }

        // TextStyle rows — fixed set, same as legacy DialogEmitter.EmitTextStyles.
        ImmutableArray<RecipeRow>.Builder tsRows = ImmutableArray.CreateBuilder<RecipeRow>(TextStyles.Length);
        for (int i = 0; i < TextStyles.Length; i++)
        {
            (string name, string faceName, int size, int? color, int styleBits) = TextStyles[i];
            tsRows.Add(new RecipeRow
            {
                Cells = ImmutableArray.Create<CellValue>(
                    new CellValue.StringValue(name),
                    new CellValue.StringValue(faceName),
                    new CellValue.IntValue(size),
                    new CellValue.IntValue(color ?? 0),
                    new CellValue.IntValue(styleBits)),
            });
        }

        // UIText rows — fixed set, same as legacy DialogEmitter.EmitUIText.
        ImmutableArray<RecipeRow>.Builder uitRows = ImmutableArray.CreateBuilder<RecipeRow>(UiTextEntries.Length);
        for (int i = 0; i < UiTextEntries.Length; i++)
        {
            (string key, string text) = UiTextEntries[i];
            uitRows.Add(new RecipeRow
            {
                Cells = ImmutableArray.Create<CellValue>(
                    new CellValue.StringValue(key),
                    new CellValue.StringValue(text)),
            });
        }

        ImmutableArray<RecipeTable>.Builder tableBuilder = ImmutableArray.CreateBuilder<RecipeTable>(7);

        tableBuilder.Add(MakeTable(DialogSchema,           dialogRows.ToImmutable(),  MsiTableDefinitions.CreateDialogTable));
        tableBuilder.Add(MakeTable(ControlSchema,          controlRows.ToImmutable(), MsiTableDefinitions.CreateControlTable));
        tableBuilder.Add(MakeTable(ControlEventSchema,     ceRows.ToImmutable(),      MsiTableDefinitions.CreateControlEventTable));
        tableBuilder.Add(MakeTable(ControlConditionSchema, ccRows.ToImmutable(),      MsiTableDefinitions.CreateControlConditionTable));
        tableBuilder.Add(MakeTable(EventMappingSchema,     emRows.ToImmutable(),      MsiTableDefinitions.CreateEventMappingTable));
        tableBuilder.Add(MakeTable(TextStyleSchema,        tsRows.ToImmutable(),      MsiTableDefinitions.CreateTextStyleTable));
        tableBuilder.Add(MakeTable(UITextSchema,           uitRows.ToImmutable(),     MsiTableDefinitions.CreateUITextTable));

        return Result<ImmutableArray<RecipeTable>>.Success(tableBuilder.ToImmutable());
    }

    // ── Extension inserted-step emission ─────────────────────────────────────────

    /// <summary>
    /// Builds and appends the <see cref="MsiDialogModel"/> for each extension-contributed step
    /// named by <see cref="DialogCustomizationModel.InsertedSteps"/> that resolves to an
    /// MSI-capable builder. Each distinct step is emitted once; duplicate insert points (the same
    /// step inserted after two stock dialogs) do not duplicate the dialog rows.
    /// </summary>
    private void AppendInsertedExtensionStepDialogs(PackageModel package, List<MsiDialogModel> dialogs)
    {
        if (_extensionStepBuilders.Count == 0
            || package.DialogCustomization is not { } customization
            || customization.InsertedSteps.IsDefaultOrEmpty)
        {
            return;
        }

        // Single registry serves both the name→builder lookup and the DialogBuildContext.
        // The Contains guard tolerates a duplicate-named builder rather than throwing.
        var registry = new DialogStepRegistry();
        for (int i = 0; i < _extensionStepBuilders.Count; i++)
        {
            if (!registry.Contains(_extensionStepBuilders[i].Name))
            {
                registry.Register(_extensionStepBuilders[i]);
            }
        }
        registry.Freeze();

        DialogBuildContext context = DialogBuildContext.Create(customization, registry);

        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (InsertedDialogStep step in customization.InsertedSteps)
        {
            if (registry.TryGet(step.StepName, out IMsiDialogStepBuilder? builder)
                && builder is not null
                && emitted.Add(step.StepName))
            {
                dialogs.Add(builder.Build(context));
            }
        }
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

    // ── Localization resolution (mirrors legacy DialogEmitter.BuildStringResolver) ────

    private static Result<Unit> ResolveLocalizationRefs(
        IReadOnlyList<MsiDialogModel> dialogs,
        PackageModel package)
    {
        Localization.LocalizedStringResolver? resolver;

        if (package.LocalizationData.Count == 0)
        {
            var builder = new Localization.LocalizationBuilder();
            builder.AddBuiltInCultures();
            builder.DefaultCulture("en-US");
            Result<System.Collections.Generic.IReadOnlyList<Localization.LocalizationModel>> buildResult = builder.Build();
            if (buildResult.IsFailure)
            {
                return Result<Unit>.Failure(buildResult.Error);
            }

            // Pass built-in list directly — no projection needed, types already match.
            resolver = new Localization.LocalizedStringResolver(buildResult.Value, "en-US");
        }
        else
        {
            // Index-based loop — avoids LINQ enumerator and delegate allocation.
            IReadOnlyList<Models.LocalizationData> locData = package.LocalizationData;
            var locModels = new List<Localization.LocalizationModel>(locData.Count);
            for (int i = 0; i < locData.Count; i++)
            {
                locModels.Add(new Localization.LocalizationModel
                {
                    Culture = locData[i].Culture,
                    Strings = locData[i].Strings,
                });
            }
            resolver = new Localization.LocalizedStringResolver(locModels, locModels[0].Culture);
        }

        for (int di = 0; di < dialogs.Count; di++)
        {
            MsiDialogModel dialog = dialogs[di];
            for (int ci = 0; ci < dialog.Controls.Count; ci++)
            {
                MsiControlModel control = dialog.Controls[ci];
                if (control.Text is not null && control.Text.Contains("!(loc."))
                {
                    Result<string> r = resolver.Resolve(control.Text);
                    if (r.IsFailure)
                    {
                        return Result<Unit>.Failure(r.Error);
                    }

                    control.Text = r.Value;
                }
            }
        }

        return Unit.Value;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CellValue StringOrNull(string? value)
        => value is not null ? new CellValue.StringValue(value) : new CellValue.Null();

    private static RecipeTable MakeTable(
        TableSchema schema,
        ImmutableArray<RecipeRow> rows,
        string createSql)
    {
        string insertSql = BuildInsertViewSql(schema);

        return new RecipeTable
        {
            Name = schema.Name,
            Columns = schema.Columns,
            Rows = rows,
            PrimaryKey = schema.PrimaryKey,
            CreateTableSql = createSql,
            InsertViewSql = insertSql,
            ForeignKeys = schema.ForeignKeys,
        };
    }

    private static string BuildInsertViewSql(TableSchema schema)
    {
        // Pre-size to avoid realloc on typical column counts.
        StringBuilder sb = new(128);
        sb.Append("SELECT ");
        ImmutableArray<RecipeColumn> cols = schema.Columns;
        for (int i = 0; i < cols.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append('`').Append(cols[i].Name).Append('`');
        }

        sb.Append(" FROM `").Append(schema.Name.Value).Append('`');
        return sb.ToString();
    }

}
