using System.Collections.Immutable;
using System.Text;
using FalkForge.Compiler.Msi.Tables;
using FalkForge.Compiler.Msi.UI;
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
/// <see cref="DialogEmitter"/> remains untouched; this producer is a parallel
/// recipe-pipeline track.
/// </para>
///
/// <para>
/// Localization: <c>!(loc.X)</c> references in control text are resolved via
/// the built-in en-US strings (same fallback <see cref="DialogEmitter"/> uses)
/// before the rows are frozen into immutable cells. The resolver runs on the
/// mutable <see cref="MsiDialogModel"/> list returned by the template, so the
/// resolution is non-destructive to the original model objects.
/// </para>
///
/// <para>
/// Thread-safety: not required — recipe build is single-threaded.
/// </para>
/// </summary>
internal sealed class DialogSetProducer : IMultiTableProducer
{
    // ── Fixed text style rows — identical to DialogEmitter.EmitTextStyles ──────
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

    // ── Fixed UIText rows — identical to DialogEmitter.EmitUIText ─────────────
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

        if (dialogSet == MsiDialogSet.None)
        {
            return Result<ImmutableArray<RecipeTable>>.Success(
                ImmutableArray<RecipeTable>.Empty);
        }

        // Resolve dialogs via the existing template infrastructure (pure data, no DB).
        IDialogTemplate template = GetTemplate(dialogSet);
        IReadOnlyList<MsiDialogModel> dialogs = template.GetDialogs(package);

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

        // TextStyle rows — fixed set, same as DialogEmitter.EmitTextStyles.
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

        // UIText rows — fixed set, same as DialogEmitter.EmitUIText.
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

    // ── Template selection (mirrors DialogEmitter.GetTemplate) ────────────────

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

    // ── Localization resolution (mirrors DialogEmitter.BuildStringResolver) ────

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

    // ── Schema builders ───────────────────────────────────────────────────────

    private static TableSchema BuildDialogSchema()
    {
        // Dialog DDL: `Dialog` CHAR(72) NN, `HCentering` SHORT NN, `VCentering` SHORT NN,
        // `Width` SHORT NN, `Height` SHORT NN, `Attributes` LONG, `Title` CHAR(128) LOC,
        // `Control_First` CHAR(50) NN, `Control_Default` CHAR(50), `Control_Cancel` CHAR(50)
        // PRIMARY KEY `Dialog`
        ImmutableArray<RecipeColumn> cols = ImmutableArray.Create(
            new RecipeColumn { Name = "Dialog",          Type = ColumnType.String,  Width = 72,  Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "HCentering",      Type = ColumnType.Integer, Width = 2,   Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "VCentering",      Type = ColumnType.Integer, Width = 2,   Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Width",           Type = ColumnType.Integer, Width = 2,   Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Height",          Type = ColumnType.Integer, Width = 2,   Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Attributes",      Type = ColumnType.Integer, Width = 4,   Nullable = true,  LocalizableKey = false },
            new RecipeColumn { Name = "Title",           Type = ColumnType.String,  Width = 128, Nullable = true,  LocalizableKey = true  },
            new RecipeColumn { Name = "Control_First",   Type = ColumnType.String,  Width = 50,  Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Control_Default", Type = ColumnType.String,  Width = 50,  Nullable = true,  LocalizableKey = false },
            new RecipeColumn { Name = "Control_Cancel",  Type = ColumnType.String,  Width = 50,  Nullable = true,  LocalizableKey = false });

        return new TableSchema
        {
            Name = TableId.Create("Dialog").Value,
            Columns = cols,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };
    }

    private static TableSchema BuildControlSchema()
    {
        // Control DDL: Dialog_ CHAR(72) NN, Control CHAR(50) NN, Type CHAR(20) NN,
        // X SHORT NN, Y SHORT NN, Width SHORT NN, Height SHORT NN, Attributes LONG,
        // Property CHAR(50), Text LONGCHAR LOC, Control_Next CHAR(50), Help CHAR(255) LOC
        // PRIMARY KEY Dialog_, Control
        ImmutableArray<RecipeColumn> cols = ImmutableArray.Create(
            new RecipeColumn { Name = "Dialog_",       Type = ColumnType.String,  Width = 72,   Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Control",       Type = ColumnType.String,  Width = 50,   Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Type",          Type = ColumnType.String,  Width = 20,   Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "X",             Type = ColumnType.Integer, Width = 2,    Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Y",             Type = ColumnType.Integer, Width = 2,    Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Width",         Type = ColumnType.Integer, Width = 2,    Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Height",        Type = ColumnType.Integer, Width = 2,    Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Attributes",    Type = ColumnType.Integer, Width = 4,    Nullable = true,  LocalizableKey = false },
            new RecipeColumn { Name = "Property",      Type = ColumnType.String,  Width = 50,   Nullable = true,  LocalizableKey = false },
            new RecipeColumn { Name = "Text",          Type = ColumnType.String,  Width = 0,    Nullable = true,  LocalizableKey = true  }, // LONGCHAR
            new RecipeColumn { Name = "Control_Next",  Type = ColumnType.String,  Width = 50,   Nullable = true,  LocalizableKey = false },
            new RecipeColumn { Name = "Help",          Type = ColumnType.String,  Width = 255,  Nullable = true,  LocalizableKey = true  });

        return new TableSchema
        {
            Name = TableId.Create("Control").Value,
            Columns = cols,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0), new ColumnIndex(1)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };
    }

    private static TableSchema BuildControlEventSchema()
    {
        // ControlEvent DDL: Dialog_ CHAR(72) NN, Control_ CHAR(50) NN,
        // Event CHAR(50) NN, Argument CHAR(255) NN, Condition CHAR(255), Ordering SHORT
        // PRIMARY KEY Dialog_, Control_, Event, Argument, Condition
        ImmutableArray<RecipeColumn> cols = ImmutableArray.Create(
            new RecipeColumn { Name = "Dialog_",   Type = ColumnType.String,  Width = 72,  Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Control_",  Type = ColumnType.String,  Width = 50,  Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Event",     Type = ColumnType.String,  Width = 50,  Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Argument",  Type = ColumnType.String,  Width = 255, Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Condition", Type = ColumnType.String,  Width = 255, Nullable = true,  LocalizableKey = false },
            new RecipeColumn { Name = "Ordering",  Type = ColumnType.Integer, Width = 2,   Nullable = true,  LocalizableKey = false });

        return new TableSchema
        {
            Name = TableId.Create("ControlEvent").Value,
            Columns = cols,
            PrimaryKey = ImmutableArray.Create(
                new ColumnIndex(0), new ColumnIndex(1), new ColumnIndex(2),
                new ColumnIndex(3), new ColumnIndex(4)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };
    }

    private static TableSchema BuildControlConditionSchema()
    {
        // ControlCondition DDL: Dialog_ CHAR(72) NN, Control_ CHAR(50) NN,
        // Action CHAR(50) NN, Condition CHAR(255) NN
        // PRIMARY KEY Dialog_, Control_, Action, Condition
        ImmutableArray<RecipeColumn> cols = ImmutableArray.Create(
            new RecipeColumn { Name = "Dialog_",   Type = ColumnType.String, Width = 72,  Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Control_",  Type = ColumnType.String, Width = 50,  Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Action",    Type = ColumnType.String, Width = 50,  Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Condition", Type = ColumnType.String, Width = 255, Nullable = false, LocalizableKey = false });

        return new TableSchema
        {
            Name = TableId.Create("ControlCondition").Value,
            Columns = cols,
            PrimaryKey = ImmutableArray.Create(
                new ColumnIndex(0), new ColumnIndex(1),
                new ColumnIndex(2), new ColumnIndex(3)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };
    }

    private static TableSchema BuildEventMappingSchema()
    {
        // EventMapping DDL: Dialog_ CHAR(72) NN, Control_ CHAR(50) NN,
        // Event CHAR(50) NN, Attribute CHAR(50) NN
        // PRIMARY KEY Dialog_, Control_, Event
        ImmutableArray<RecipeColumn> cols = ImmutableArray.Create(
            new RecipeColumn { Name = "Dialog_",   Type = ColumnType.String, Width = 72, Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Control_",  Type = ColumnType.String, Width = 50, Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Event",     Type = ColumnType.String, Width = 50, Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Attribute", Type = ColumnType.String, Width = 50, Nullable = false, LocalizableKey = false });

        return new TableSchema
        {
            Name = TableId.Create("EventMapping").Value,
            Columns = cols,
            PrimaryKey = ImmutableArray.Create(
                new ColumnIndex(0), new ColumnIndex(1), new ColumnIndex(2)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };
    }

    private static TableSchema BuildTextStyleSchema()
    {
        // TextStyle DDL: TextStyle CHAR(72) NN, FaceName CHAR(32) NN,
        // Size SHORT NN, Color LONG, StyleBits SHORT
        // PRIMARY KEY TextStyle
        ImmutableArray<RecipeColumn> cols = ImmutableArray.Create(
            new RecipeColumn { Name = "TextStyle",  Type = ColumnType.String,  Width = 72, Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "FaceName",   Type = ColumnType.String,  Width = 32, Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Size",       Type = ColumnType.Integer, Width = 2,  Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Color",      Type = ColumnType.Integer, Width = 4,  Nullable = true,  LocalizableKey = false },
            new RecipeColumn { Name = "StyleBits",  Type = ColumnType.Integer, Width = 2,  Nullable = true,  LocalizableKey = false });

        return new TableSchema
        {
            Name = TableId.Create("TextStyle").Value,
            Columns = cols,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };
    }

    private static TableSchema BuildUITextSchema()
    {
        // UIText DDL: Key CHAR(72) NN, Text CHAR(255) LOC
        // PRIMARY KEY Key
        ImmutableArray<RecipeColumn> cols = ImmutableArray.Create(
            new RecipeColumn { Name = "Key",  Type = ColumnType.String, Width = 72,  Nullable = false, LocalizableKey = false },
            new RecipeColumn { Name = "Text", Type = ColumnType.String, Width = 255, Nullable = true,  LocalizableKey = true  });

        return new TableSchema
        {
            Name = TableId.Create("UIText").Value,
            Columns = cols,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };
    }
}
