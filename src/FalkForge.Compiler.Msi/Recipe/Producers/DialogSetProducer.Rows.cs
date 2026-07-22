using System.Collections.Immutable;
using System.Text;
using FalkForge.Compiler.Msi.Tables;
using FalkForge.Compiler.Msi.UI;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

// Turns the composed dialog list into the seven MSI UI recipe tables (Dialog, Control,
// ControlEvent, ControlCondition, EventMapping, TextStyle, UIText).
internal sealed partial class DialogSetProducer
{
    /// <summary>
    /// Builds all seven MSI UI tables from <paramref name="dialogs"/>: one row per dialog/control/
    /// event/condition/mapping, plus the fixed <see cref="TextStyles"/> and <see cref="UiTextEntries"/>
    /// rows. Called once <paramref name="dialogs"/> has been composed, localized, and had its license
    /// text injected.
    /// </summary>
    private static ImmutableArray<RecipeTable> BuildDialogTables(List<MsiDialogModel> dialogs)
    {
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

        return tableBuilder.ToImmutable();
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
