using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Multi-table producer that emits one <see cref="RecipeTable"/> per
/// <see cref="CustomTableModel"/> defined on the package. Unlike the fixed
/// built-in producers, the number and schema of tables are known only at
/// build time, which is why this producer implements <see cref="IMultiTableProducer"/>
/// rather than <see cref="ITableProducer"/>.
///
/// Identifier validation is performed here as defense-in-depth (the upstream
/// <see cref="FalkForge.Core.Validation.ModelValidator"/> already runs
/// <c>ValidateCustomTables</c>, but proximity to SQL emission requires an
/// independent guard). The same regex used by <see cref="TableId"/> and
/// <see cref="RecipeColumn"/> enforces the MSI identifier contract:
/// <c>^[A-Za-z_][A-Za-z0-9_]{0,30}$</c>.
///
/// Binary columns: a <see cref="CellValue.StreamRef"/> is emitted and the
/// cell value (a <c>byte[]</c>) is registered in the shared
/// <see cref="IStreamRegistry"/>. The stream key is built from the table
/// name and the primary-key cell values of the row to ensure uniqueness
/// across multi-row binary tables.
///
/// Thread-safety: not required — recipe build is single-threaded.
/// </summary>
internal sealed class CustomTablesProducer : IMultiTableProducer
{
    // Same pattern as TableId and RecipeColumn — MSI identifier rules.
    private static readonly Regex SafeIdentifierPattern = new(
        "^[A-Za-z_][A-Za-z0-9_]{0,30}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    // FrozenDictionary for O(1) column-type lookup. Built once at class init.
    private static readonly FrozenDictionary<CustomTableColumnType, ColumnType> ColumnTypeMap =
        new Dictionary<CustomTableColumnType, ColumnType>
        {
            [CustomTableColumnType.String] = ColumnType.String,
            [CustomTableColumnType.Int16] = ColumnType.Integer,
            [CustomTableColumnType.Int32] = ColumnType.Integer,
            [CustomTableColumnType.Binary] = ColumnType.Binary,
            [CustomTableColumnType.Stream] = ColumnType.Binary,
        }.ToFrozenDictionary();

    /// <inheritdoc/>
    public Result<ImmutableArray<RecipeTable>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IReadOnlyList<CustomTableModel> customTables = context.Resolved.Package.CustomTables;

        if (customTables.Count == 0)
        {
            return Result<ImmutableArray<RecipeTable>>.Success(ImmutableArray<RecipeTable>.Empty);
        }

        // Validate all identifiers before allocating any output.
        Result<Unit> validationResult = ValidateIdentifiers(customTables);
        if (validationResult.IsFailure)
        {
            return Result<ImmutableArray<RecipeTable>>.Failure(validationResult.Error);
        }

        ImmutableArray<RecipeTable>.Builder tableBuilder =
            ImmutableArray.CreateBuilder<RecipeTable>(customTables.Count);

        // Index-based loop avoids IReadOnlyList<T> enumerator heap allocation (HAA0401).
        for (int i = 0; i < customTables.Count; i++)
        {
            Result<RecipeTable> tableResult = BuildTable(customTables[i], context.Streams);
            if (tableResult.IsFailure)
            {
                return Result<ImmutableArray<RecipeTable>>.Failure(tableResult.Error);
            }

            tableBuilder.Add(tableResult.Value);
        }

        return Result<ImmutableArray<RecipeTable>>.Success(tableBuilder.ToImmutable());
    }

    // ── Identifier validation ─────────────────────────────────────────────────

    /// <summary>
    /// Defense-in-depth: validates all table and column names before any
    /// SQL or RecipeTable construction. Matches the guard in legacy
    /// <c>TableEmitter.ValidateCustomTableIdentifiers</c> but uses the
    /// MSI-max-length-aware regex (31 chars) consistent with
    /// <see cref="TableId"/> and <see cref="RecipeColumn"/>.
    /// </summary>
    private static Result<Unit> ValidateIdentifiers(IReadOnlyList<CustomTableModel> tables)
    {
        // Index-based loops avoid IReadOnlyList<T> enumerator heap allocation (HAA0401).
        for (int ti = 0; ti < tables.Count; ti++)
        {
            CustomTableModel table = tables[ti];
            if (!IsValidIdentifier(table.Name))
            {
                return Result<Unit>.Failure(
                    ErrorKind.CompilationError,
                    $"Custom table name '{table.Name}' is not a valid MSI identifier. " +
                    "Table names must match ^[A-Za-z_][A-Za-z0-9_]{0,30}$.");
            }

            // Index-based loop avoids IReadOnlyList<T> enumerator heap allocation (HAA0401).
            for (int ci = 0; ci < table.Columns.Count; ci++)
            {
                CustomTableColumnModel col = table.Columns[ci];
                if (!IsValidIdentifier(col.Name))
                {
                    return Result<Unit>.Failure(
                        ErrorKind.CompilationError,
                        $"Column name '{col.Name}' in custom table '{table.Name}' is not a valid MSI identifier. " +
                        "Column names must match ^[A-Za-z_][A-Za-z0-9_]{0,30}$.");
                }
            }
        }

        return Unit.Value;
    }

    private static bool IsValidIdentifier(string? name)
        => !string.IsNullOrWhiteSpace(name) && SafeIdentifierPattern.IsMatch(name);

    // ── Table construction ────────────────────────────────────────────────────

    private static Result<RecipeTable> BuildTable(CustomTableModel model, IStreamRegistry streams)
    {
        // Build columns.
        ImmutableArray<RecipeColumn>.Builder colBuilder =
            ImmutableArray.CreateBuilder<RecipeColumn>(model.Columns.Count);

        ImmutableArray<ColumnIndex>.Builder pkBuilder =
            ImmutableArray.CreateBuilder<ColumnIndex>();

        for (int i = 0; i < model.Columns.Count; i++)
        {
            CustomTableColumnModel col = model.Columns[i];
            ColumnType recipeType = ColumnTypeMap[col.Type];

            colBuilder.Add(new RecipeColumn
            {
                Name = col.Name,
                Type = recipeType,
                Width = col.Width,
                Nullable = col.Nullable,
                LocalizableKey = false,
            });

            if (col.PrimaryKey)
            {
                pkBuilder.Add(new ColumnIndex(i));
            }
        }

        ImmutableArray<RecipeColumn> columns = colBuilder.ToImmutable();

        // Ensure at least one PK column. MSI requires a primary key.
        if (pkBuilder.Count == 0)
        {
            pkBuilder.Add(new ColumnIndex(0));
        }

        ImmutableArray<ColumnIndex> primaryKey = pkBuilder.ToImmutable();

        // Wrap the TableId (validates name again — belt-and-suspenders).
        Result<TableId> tableIdResult = TableId.Create(model.Name);
        if (tableIdResult.IsFailure)
        {
            return Result<RecipeTable>.Failure(tableIdResult.Error);
        }

        TableId tableId = tableIdResult.Value;

        // Build rows.
        ImmutableArray<RecipeRow> rows = BuildRows(model, columns, tableId, streams);

        // Build SQL strings.
        string createSql = BuildCreateTableSql(tableId, model.Columns);
        string insertSql = BuildInsertViewSql(tableId, columns);

        RecipeTable table = new()
        {
            Name = tableId,
            Columns = columns,
            Rows = rows,
            PrimaryKey = primaryKey,
            CreateTableSql = createSql,
            InsertViewSql = insertSql,
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
            IsBuiltIn = false,
        };

        return Result<RecipeTable>.Success(table);
    }

    // ── Row mapping ───────────────────────────────────────────────────────────

    private static ImmutableArray<RecipeRow> BuildRows(
        CustomTableModel model,
        ImmutableArray<RecipeColumn> columns,
        TableId tableId,
        IStreamRegistry streams)
    {
        if (model.Rows.Count == 0)
        {
            return ImmutableArray<RecipeRow>.Empty;
        }

        ImmutableArray<RecipeRow>.Builder rowBuilder =
            ImmutableArray.CreateBuilder<RecipeRow>(model.Rows.Count);

        for (int rowIndex = 0; rowIndex < model.Rows.Count; rowIndex++)
        {
            Dictionary<string, object?> rowDict = model.Rows[rowIndex];
            ImmutableArray<CellValue>.Builder cellBuilder =
                ImmutableArray.CreateBuilder<CellValue>(columns.Length);

            for (int colIndex = 0; colIndex < model.Columns.Count; colIndex++)
            {
                CustomTableColumnModel colModel = model.Columns[colIndex];

                bool hasValue = rowDict.TryGetValue(colModel.Name, out object? rawValue)
                    && rawValue is not null;

                CellValue cell;
                if (!hasValue)
                {
                    // Absent or null key — emit null for nullable columns.
                    cell = new CellValue.Null();
                }
                else if (colModel.Type is CustomTableColumnType.Binary or CustomTableColumnType.Stream)
                {
                    // Register the binary payload and emit a StreamRef.
                    // Stream key: "{TableName}.{RowIndex}.{ColumnName}" for uniqueness.
                    string streamKey = $"{tableId.Value}.{rowIndex}.{colModel.Name}";
                    byte[] payload = (byte[])rawValue!;
                    byte[] sha256 = SHA256.HashData(payload);
                    streams.Register(streamKey, new StreamSource.InMemory(payload, sha256));
                    cell = new CellValue.StreamRef(streamKey);
                }
                else if (colModel.Type is CustomTableColumnType.Int16 or CustomTableColumnType.Int32)
                {
                    cell = new CellValue.IntValue(Convert.ToInt32(rawValue!, System.Globalization.CultureInfo.InvariantCulture));
                }
                else
                {
                    // String / fallback.
                    cell = new CellValue.StringValue(rawValue!.ToString()!);
                }

                cellBuilder.Add(cell);
            }

            rowBuilder.Add(new RecipeRow { Cells = cellBuilder.ToImmutable() });
        }

        return rowBuilder.ToImmutable();
    }

    // ── SQL generation ────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the CREATE TABLE SQL for a custom table. Identifiers are
    /// backtick-quoted to match MSI database conventions. Type tokens match
    /// those produced by legacy <c>TableEmitter.EmitCustomTables</c>:
    /// SHORT / LONG / OBJECT / CHAR(n).
    /// </summary>
    private static string BuildCreateTableSql(TableId tableId, IReadOnlyList<CustomTableColumnModel> columns)
    {
        // Pre-size StringBuilder to avoid re-allocation for typical tables.
        StringBuilder sb = new(128);
        sb.Append("CREATE TABLE `").Append(tableId.Value).Append("` (");

        List<string> pkNames = [];

        for (int i = 0; i < columns.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            CustomTableColumnModel col = columns[i];
            string typeSql = col.Type switch
            {
                CustomTableColumnType.Int16 => "SHORT",
                CustomTableColumnType.Int32 => "LONG",
                CustomTableColumnType.Binary or CustomTableColumnType.Stream => "OBJECT",
                _ => $"CHAR({col.Width})",
            };

            sb.Append('`').Append(col.Name).Append("` ").Append(typeSql);
            if (!col.Nullable)
            {
                sb.Append(" NOT NULL");
            }

            if (col.PrimaryKey)
            {
                pkNames.Add(col.Name);
            }
        }

        // MSI SQL syntax: PRIMARY KEY clause is inside the parentheses, after the last
        // column definition — no comma before it. Matches legacy TableEmitter.EmitCustomTables
        // and the MsiTableDefinitions constants (e.g., "... NOT NULL PRIMARY KEY `col`").
        // Placing it outside the parens produces error 1615 from msi.dll.
        if (pkNames.Count > 0)
        {
            sb.Append(" PRIMARY KEY ");
            for (int i = 0; i < pkNames.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append('`').Append(pkNames[i]).Append('`');
            }
        }

        sb.Append(')');

        return sb.ToString();
    }

    /// <summary>
    /// Builds the SELECT SQL used as the insert-view for the MSI executor:
    /// <c>SELECT `c1`, `c2` FROM `Table`</c>.
    /// </summary>
    private static string BuildInsertViewSql(TableId tableId, ImmutableArray<RecipeColumn> columns)
    {
        StringBuilder sb = new(64);
        sb.Append("SELECT ");

        for (int i = 0; i < columns.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append('`').Append(columns[i].Name).Append('`');
        }

        sb.Append(" FROM `").Append(tableId.Value).Append('`');
        return sb.ToString();
    }
}
