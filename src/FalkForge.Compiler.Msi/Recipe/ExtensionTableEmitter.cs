using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FalkForge.Diagnostics;
using FalkForge.Extensibility;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Routes rows contributed by <see cref="IMsiTableContributor"/> instances into the
/// recipe. This is the wiring that was missing for the whole "phase 11" period: prior
/// to it, registered contributors were collected and then silently discarded, so
/// extension-authored MSI tables (SqlDatabase, WixFirewallException, IIS config, …)
/// never reached the compiled MSI.
///
/// <para>Two emission modes, chosen per contributor by table name:</para>
/// <list type="bullet">
///   <item><description>
///     <b>Built-in table</b> — <see cref="IMsiTableContributor.TableName"/> matches a table
///     already produced by the fixed built-in pipeline (e.g. <c>CustomAction</c>,
///     <c>Registry</c>). Rows are mapped against that table's known
///     <see cref="RecipeColumn"/> set and appended to it, in contributor-registration order,
///     after the built-in rows. The merged table is still subject to PK/FK validation.
///   </description></item>
///   <item><description>
///     <b>Custom table</b> — the table is not built-in. The contributor must declare its
///     columns via <see cref="IMsiTableContributor.WriteColumns"/>; the emitter issues the
///     <c>CREATE TABLE</c>/insert SQL from that schema. A contributor that yields rows for a
///     custom table without a write schema <b>fails the build loudly</b> — never silently
///     drops the rows.
///   </description></item>
/// </list>
///
/// <para>
/// Determinism: contributors run in registration order, rows in the order the contributor
/// returns them, columns in declaration order — so the emitted tables are byte-stable for
/// reproducible builds. Identifiers (table and column names) are re-validated here against
/// the MSI identifier grammar as defense-in-depth before any SQL is built, matching the
/// guard in <see cref="Producers.CustomTablesProducer"/>.
/// </para>
/// </summary>
internal static class ExtensionTableEmitter
{
    private static readonly Regex SafeIdentifierPattern = new(
        "^[A-Za-z_][A-Za-z0-9_]{0,30}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    /// <summary>Outcome of routing contributor rows: the (possibly merged) built-in tables and any new custom tables.</summary>
    internal readonly record struct EmissionOutcome(
        ImmutableArray<RecipeTable> BuiltInTables,
        ImmutableArray<RecipeTable> CustomTables);

    /// <summary>
    /// Routes every contributor's rows into either the matching built-in table (merge) or a new
    /// custom table (create). Returns the built-in tables in their original order (with merged
    /// rows appended where applicable) plus the ordered list of custom tables. When
    /// <paramref name="contributors"/> is empty the built-in tables pass through unchanged.
    /// </summary>
    internal static FalkForge.Result<EmissionOutcome> Emit(
        IReadOnlyList<IMsiTableContributor> contributors,
        ImmutableArray<RecipeTable> builtInTables,
        ExtensionContext context,
        IStreamRegistry streams,
        IFalkLogger? logger)
    {
        ArgumentNullException.ThrowIfNull(contributors);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(streams);

        if (contributors.Count == 0)
        {
            return FalkForge.Result<EmissionOutcome>.Success(
                new EmissionOutcome(builtInTables, ImmutableArray<RecipeTable>.Empty));
        }

        // Index the built-in tables by name so a contributor can target one for a merge.
        var builtInIndex = new Dictionary<string, int>(builtInTables.Length, StringComparer.Ordinal);
        for (int i = 0; i < builtInTables.Length; i++)
            builtInIndex[builtInTables[i].Name.Value] = i;

        // Accumulators. mergedRows keeps appended rows per built-in table index; custom
        // tables preserve first-seen order so output is deterministic.
        var mergedRows = new Dictionary<int, ImmutableArray<RecipeRow>.Builder>();
        var customOrder = new List<string>();
        var customColumns = new Dictionary<string, ImmutableArray<RecipeColumn>>(StringComparer.Ordinal);
        var customPrimaryKey = new Dictionary<string, ImmutableArray<ColumnIndex>>(StringComparer.Ordinal);
        var customRows = new Dictionary<string, ImmutableArray<RecipeRow>.Builder>(StringComparer.Ordinal);

        bool logDebug = logger is not null && logger.MinimumLevel <= LogLevel.Debug;

        for (int c = 0; c < contributors.Count; c++)
        {
            IMsiTableContributor contributor = contributors[c];
            string tableName = contributor.TableName;

            IReadOnlyList<MsiTableRow> rows = contributor.GetRows(context) ?? [];
            if (rows.Count == 0)
            {
                if (logDebug)
                    logger!.Debug("ExtensionTableEmitter",
                        $"Contributor for table '{tableName}' produced 0 rows; nothing to emit.");
                continue;
            }

            if (builtInIndex.TryGetValue(tableName, out int idx))
            {
                // Merge into an existing built-in table using its known column schema.
                RecipeTable target = builtInTables[idx];
                if (!mergedRows.TryGetValue(idx, out ImmutableArray<RecipeRow>.Builder? builder))
                {
                    builder = ImmutableArray.CreateBuilder<RecipeRow>(rows.Count);
                    mergedRows[idx] = builder;
                }

                FalkForge.Result<Unit> mergeResult = MapRows(
                    rows, target.Name, target.Columns, streams, builder);
                if (mergeResult.IsFailure)
                    return FalkForge.Result<EmissionOutcome>.Failure(mergeResult.Error);

                if (logDebug)
                    logger!.Debug("ExtensionTableEmitter",
                        $"Merged {rows.Count} contributed row(s) into built-in table '{tableName}'.");

                continue;
            }

            // Custom table: a write schema is mandatory — otherwise the rows cannot be emitted.
            IReadOnlyList<ContributedColumn>? writeColumns = contributor.WriteColumns;
            if (writeColumns is null || writeColumns.Count == 0)
            {
                string message =
                    $"Extension contributor for table '{tableName}' produced {rows.Count} row(s) but declared no " +
                    "WriteColumns schema, and '" + tableName + "' is not a built-in MSI table. The rows cannot be " +
                    "emitted. Declare IMsiTableContributor.WriteColumns for this table, or target a built-in table.";
                logger?.Log(LogLevel.Error, "ExtensionTableEmitter", message,
                    new Dictionary<string, string> { ["code"] = "EXT001" });
                return FalkForge.Result<EmissionOutcome>.Failure(ErrorKind.CompilationError, message);
            }

            if (!customRows.TryGetValue(tableName, out ImmutableArray<RecipeRow>.Builder? rowBuilder))
            {
                FalkForge.Result<(ImmutableArray<RecipeColumn> Columns, ImmutableArray<ColumnIndex> Pk)> schema =
                    BuildCustomSchema(tableName, writeColumns);
                if (schema.IsFailure)
                    return FalkForge.Result<EmissionOutcome>.Failure(schema.Error);

                customOrder.Add(tableName);
                customColumns[tableName] = schema.Value.Columns;
                customPrimaryKey[tableName] = schema.Value.Pk;
                rowBuilder = ImmutableArray.CreateBuilder<RecipeRow>(rows.Count);
                customRows[tableName] = rowBuilder;
            }

            FalkForge.Result<TableId> tableIdResult = TableId.Create(tableName);
            if (tableIdResult.IsFailure)
                return FalkForge.Result<EmissionOutcome>.Failure(tableIdResult.Error);

            FalkForge.Result<Unit> rowResult = MapRows(
                rows, tableIdResult.Value, customColumns[tableName], streams, rowBuilder);
            if (rowResult.IsFailure)
                return FalkForge.Result<EmissionOutcome>.Failure(rowResult.Error);

            if (logDebug)
                logger!.Debug("ExtensionTableEmitter",
                    $"Emitted {rows.Count} contributed row(s) into custom table '{tableName}'.");
        }

        // Apply merges to the built-in tables, preserving order.
        ImmutableArray<RecipeTable> resultBuiltIns;
        if (mergedRows.Count == 0)
        {
            resultBuiltIns = builtInTables;
        }
        else
        {
            ImmutableArray<RecipeTable>.Builder b = ImmutableArray.CreateBuilder<RecipeTable>(builtInTables.Length);
            for (int i = 0; i < builtInTables.Length; i++)
            {
                RecipeTable t = builtInTables[i];
                if (mergedRows.TryGetValue(i, out ImmutableArray<RecipeRow>.Builder? extra) && extra.Count > 0)
                    t = t with { Rows = t.Rows.AddRange(extra.ToImmutable()) };
                b.Add(t);
            }

            resultBuiltIns = b.ToImmutable();
        }

        // Materialize the custom tables in first-seen order.
        ImmutableArray<RecipeTable>.Builder customBuilder =
            ImmutableArray.CreateBuilder<RecipeTable>(customOrder.Count);
        foreach (string name in customOrder)
        {
            FalkForge.Result<TableId> tableIdResult = TableId.Create(name);
            if (tableIdResult.IsFailure)
                return FalkForge.Result<EmissionOutcome>.Failure(tableIdResult.Error);

            TableId tableId = tableIdResult.Value;
            ImmutableArray<RecipeColumn> columns = customColumns[name];

            customBuilder.Add(new RecipeTable
            {
                Name = tableId,
                Columns = columns,
                Rows = customRows[name].ToImmutable(),
                PrimaryKey = customPrimaryKey[name],
                CreateTableSql = BuildCreateTableSql(tableId, columns, customPrimaryKey[name]),
                InsertViewSql = BuildInsertViewSql(tableId, columns),
                ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
                IsBuiltIn = false,
            });
        }

        return FalkForge.Result<EmissionOutcome>.Success(
            new EmissionOutcome(resultBuiltIns, customBuilder.ToImmutable()));
    }

    // ── Schema construction ───────────────────────────────────────────────────

    private static FalkForge.Result<(ImmutableArray<RecipeColumn> Columns, ImmutableArray<ColumnIndex> Pk)>
        BuildCustomSchema(string tableName, IReadOnlyList<ContributedColumn> writeColumns)
    {
        ImmutableArray<RecipeColumn>.Builder cols = ImmutableArray.CreateBuilder<RecipeColumn>(writeColumns.Count);
        ImmutableArray<ColumnIndex>.Builder pk = ImmutableArray.CreateBuilder<ColumnIndex>();

        for (int i = 0; i < writeColumns.Count; i++)
        {
            ContributedColumn col = writeColumns[i];
            if (!IsValidIdentifier(col.Name))
            {
                return FalkForge.Result<(ImmutableArray<RecipeColumn>, ImmutableArray<ColumnIndex>)>.Failure(
                    ErrorKind.CompilationError,
                    $"Column name '{col.Name}' in contributed table '{tableName}' is not a valid MSI identifier " +
                    "(must match ^[A-Za-z_][A-Za-z0-9_]{0,30}$).");
            }

            (ColumnType type, int width) = MapColumnType(col);
            cols.Add(new RecipeColumn
            {
                Name = col.Name,
                Type = type,
                Width = width,
                Nullable = col.Nullable && !col.PrimaryKey,
                LocalizableKey = false,
            });

            if (col.PrimaryKey)
                pk.Add(new ColumnIndex(i));
        }

        // MSI requires a primary key; fall back to the first column, mirroring CustomTablesProducer.
        if (pk.Count == 0)
            pk.Add(new ColumnIndex(0));

        return FalkForge.Result<(ImmutableArray<RecipeColumn>, ImmutableArray<ColumnIndex>)>.Success(
            (cols.ToImmutable(), pk.ToImmutable()));
    }

    private static (ColumnType Type, int Width) MapColumnType(ContributedColumn col)
        => col.Type switch
        {
            ContributedColumnType.Int16 => (ColumnType.Integer, 2),
            ContributedColumnType.Int32 => (ColumnType.Integer, 4),
            ContributedColumnType.Binary => (ColumnType.Binary, 0),
            _ => (ColumnType.String, col.Width > 0 ? col.Width : 255),
        };

    // ── Row mapping ───────────────────────────────────────────────────────────

    private static FalkForge.Result<Unit> MapRows(
        IReadOnlyList<MsiTableRow> rows,
        TableId tableId,
        ImmutableArray<RecipeColumn> columns,
        IStreamRegistry streams,
        ImmutableArray<RecipeRow>.Builder sink)
    {
        for (int r = 0; r < rows.Count; r++)
        {
            IReadOnlyDictionary<string, object?> fields = rows[r].Fields;
            ImmutableArray<CellValue>.Builder cells = ImmutableArray.CreateBuilder<CellValue>(columns.Length);

            for (int col = 0; col < columns.Length; col++)
            {
                RecipeColumn column = columns[col];
                bool present = fields.TryGetValue(column.Name, out object? raw) && raw is not null;

                if (!present)
                {
                    if (!column.Nullable)
                    {
                        return FalkForge.Result<Unit>.Failure(
                            ErrorKind.CompilationError,
                            $"Contributed table '{tableId.Value}' row {r} is missing a value for non-nullable " +
                            $"column '{column.Name}'.");
                    }

                    cells.Add(new CellValue.Null());
                    continue;
                }

                switch (column.Type)
                {
                    case ColumnType.Integer:
                        cells.Add(new CellValue.IntValue(Convert.ToInt32(raw, CultureInfo.InvariantCulture)));
                        break;

                    case ColumnType.Binary:
                        if (raw is not byte[] payload)
                        {
                            return FalkForge.Result<Unit>.Failure(
                                ErrorKind.CompilationError,
                                $"Contributed table '{tableId.Value}' row {r} column '{column.Name}' is a binary " +
                                "column but the value is not a byte[].");
                        }

                        string streamKey = $"{tableId.Value}.{r}.{column.Name}";
                        streams.Register(streamKey, new StreamSource.InMemory(payload, SHA256.HashData(payload)));
                        cells.Add(new CellValue.StreamRef(streamKey));
                        break;

                    default:
                        cells.Add(new CellValue.StringValue(
                            raw as string ?? Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty));
                        break;
                }
            }

            sink.Add(new RecipeRow { Cells = cells.ToImmutable() });
        }

        return Unit.Value;
    }

    // ── SQL generation (custom tables) ────────────────────────────────────────

    private static string BuildCreateTableSql(
        TableId tableId,
        ImmutableArray<RecipeColumn> columns,
        ImmutableArray<ColumnIndex> primaryKey)
    {
        StringBuilder sb = new(128);
        sb.Append("CREATE TABLE `").Append(tableId.Value).Append("` (");

        for (int i = 0; i < columns.Length; i++)
        {
            if (i > 0)
                sb.Append(", ");

            RecipeColumn col = columns[i];
            sb.Append('`').Append(col.Name).Append("` ").Append(TypeToken(col));
            if (!col.Nullable)
                sb.Append(" NOT NULL");
        }

        if (primaryKey.Length > 0)
        {
            sb.Append(" PRIMARY KEY ");
            for (int i = 0; i < primaryKey.Length; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append('`').Append(columns[primaryKey[i].Value].Name).Append('`');
            }
        }

        sb.Append(')');
        return sb.ToString();
    }

    private static string TypeToken(RecipeColumn col)
        => col.Type switch
        {
            ColumnType.Integer => col.Width <= 2 ? "SHORT" : "LONG",
            ColumnType.Binary => "OBJECT",
            _ => $"CHAR({(col.Width > 0 ? col.Width : 255)})",
        };

    private static string BuildInsertViewSql(TableId tableId, ImmutableArray<RecipeColumn> columns)
    {
        StringBuilder sb = new(64);
        sb.Append("SELECT ");
        for (int i = 0; i < columns.Length; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append('`').Append(columns[i].Name).Append('`');
        }

        sb.Append(" FROM `").Append(tableId.Value).Append('`');
        return sb.ToString();
    }

    private static bool IsValidIdentifier(string? name)
        => !string.IsNullOrWhiteSpace(name) && SafeIdentifierPattern.IsMatch(name);
}
