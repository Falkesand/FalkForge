using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Recipe-level validator that catches duplicate primary keys before the
/// recipe is handed to the executor. Runs after every producer has finished
/// emitting rows so that <c>msi.dll</c> never receives an
/// <c>InsertRow</c> call that would fail with
/// <c>ERROR_FUNCTION_FAILED</c> on a duplicate-key constraint.
/// </summary>
internal static class PrimaryKeyValidator
{
    /// <summary>Separator between primary-key parts in the composite-key string.</summary>
    /// <remarks>
    /// 0x1F (Unit Separator) is a control character that cannot appear in any
    /// MSI identifier or value, so concatenated keys cannot collide via stray
    /// separator content.
    /// </remarks>
    internal const char CompositeSeparator = '\x1F';

    /// <summary>
    /// Validates each table in <paramref name="tables"/> for duplicate primary
    /// keys. Returns success on first clean pass; on the first duplicate
    /// encountered, returns a <see cref="ErrorKind.Validation"/> failure naming
    /// the table and the offending key. Null cells, stream cells, and unknown
    /// cell shapes in primary-key positions are also reported as failures —
    /// the MSI Property/Component/etc. PK columns are NOT NULL by design and
    /// streams cannot legally appear in a PK position.
    /// </summary>
    public static Result<Unit> Validate(IReadOnlyList<RecipeTable> tables)
    {
        ArgumentNullException.ThrowIfNull(tables);

        for (int t = 0; t < tables.Count; t++)
        {
            RecipeTable table = tables[t];
            HashSet<string> seen = new(table.Rows.Length, StringComparer.Ordinal);
            for (int r = 0; r < table.Rows.Length; r++)
            {
                Result<string> keyResult = ComposeKey(table, table.Rows[r]);
                if (keyResult.IsFailure)
                {
                    return Result<Unit>.Failure(keyResult.Error);
                }

                string key = keyResult.Value;
                if (!seen.Add(key))
                {
                    return Result<Unit>.Failure(new Error(
                        ErrorKind.Validation,
                        $"Duplicate primary key in table {table.Name.Value}: {key}"));
                }
            }
        }

        return Result<Unit>.Success(Unit.Value);
    }

    /// <summary>
    /// Composes a single string key from the cells at the primary-key column
    /// indices. Shared with <see cref="ForeignKeyValidator"/> so the FK lookup
    /// uses the same canonical form the PK validator built.
    /// </summary>
    internal static Result<string> ComposeKey(RecipeTable table, RecipeRow row)
    {
        ImmutableArray<ColumnIndex> pk = table.PrimaryKey;
        if (pk.Length == 1)
        {
            return StringifyPkCell(table, row, pk[0]);
        }

        // StringBuilder is overkill for short PKs but readable and safe — avoids
        // string concatenation in a loop while keeping the producer-driven
        // 1..N column count flexible.
        StringBuilder builder = new();
        for (int i = 0; i < pk.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(CompositeSeparator);
            }

            Result<string> partResult = StringifyPkCell(table, row, pk[i]);
            if (partResult.IsFailure)
            {
                return partResult;
            }

            builder.Append(partResult.Value);
        }

        return Result<string>.Success(builder.ToString());
    }

    private static Result<string> StringifyPkCell(RecipeTable table, RecipeRow row, ColumnIndex pkIndex)
    {
        int idx = pkIndex.Value;
        if (idx >= row.Cells.Length)
        {
            return Result<string>.Failure(new Error(
                ErrorKind.Validation,
                $"Primary-key column index {idx} out of range for row in table {table.Name.Value}."));
        }

        CellValue cell = row.Cells[idx];
        return cell switch
        {
            CellValue.IntValue intCell => Result<string>.Success(
                intCell.Value.ToString(CultureInfo.InvariantCulture)),
            CellValue.StringValue strCell => Result<string>.Success(strCell.Value),
            CellValue.ForeignKey fkCell => Result<string>.Success(fkCell.TargetKey),
            CellValue.Null => Result<string>.Failure(new Error(
                ErrorKind.Validation,
                $"Null cell in primary-key column {idx} of table {table.Name.Value}; PK columns cannot be null.")),
            CellValue.StreamRef => Result<string>.Failure(new Error(
                ErrorKind.Validation,
                $"Stream cell in primary-key column {idx} of table {table.Name.Value}; streams cannot serve as primary keys.")),
            _ => Result<string>.Failure(new Error(
                ErrorKind.Validation,
                $"Unsupported cell type in primary-key column {idx} of table {table.Name.Value}.")),
        };
    }
}
