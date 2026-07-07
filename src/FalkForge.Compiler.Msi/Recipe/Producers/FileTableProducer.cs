using System.Collections.Immutable;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>File</c> table. Walks
/// <see cref="ResolvedPackage.Files"/> in order and emits one row per file.
/// Sequence numbers are assigned by walking the flat file list starting at
/// 1 — matches the convention in <see cref="TableEmitter.EmitFiles"/>.
/// Phase-4 scope: real cabinet-driven sequencing strategies arrive in later
/// phases via <see cref="MsiRecipeBuildOptions.Sequencing"/>; for now the
/// producer uses ordinal index. Version/Language are emitted as null since
/// <see cref="ResolvedFile"/> exposes neither today; Attributes is set to
/// 512 (msidbFileAttributesVital) to match the legacy emitter.
/// The FileName column uses the MSI short|long format when the name requires
/// 8.3 truncation — identical to the encoding in
/// <see cref="TableEmitter.EmitFiles"/> so both pipelines produce the same
/// File table row content.
/// </summary>
internal sealed class FileTableProducer : ITableProducer
{
    private const int FileAttributesVital = 512;

    /// <summary>Static schema describing the <c>File</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        TableId componentTable = WellKnownTableIds.Component;
        ImmutableArray<RecipeRow>.Builder rows = ImmutableArray.CreateBuilder<RecipeRow>();
        int sequence = 1;
        foreach (ResolvedFile file in context.Resolved.Files)
        {
            // Mirror the legacy EmitFiles short|long file name encoding:
            // if the name requires 8.3 truncation the FileName column stores
            // "SHORTNA~1.EXT|LongName.ext"; otherwise just "LongName.ext".
            string shortName = GetShortFileName(file.FileName);
            string msiFileName = shortName == file.FileName
                ? file.FileName
                : string.Concat(shortName, "|", file.FileName);

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(file.FileId),
                new CellValue.ForeignKey(componentTable, file.ComponentId),
                new CellValue.StringValue(msiFileName),
                new CellValue.IntValue(checked((int)file.FileSize)),
                new CellValue.Null(),
                new CellValue.Null(),
                new CellValue.IntValue(FileAttributesVital),
                new CellValue.IntValue(sequence));
            rows.Add(new RecipeRow { Cells = cells });
            sequence++;
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    /// <summary>
    /// Generates an 8.3 short file name for <paramref name="longName"/> using
    /// the same algorithm as <c>TableEmitter.GetShortFileName</c>. Returns
    /// <paramref name="longName"/> unchanged when no truncation is required
    /// (name ≤ 8 chars, extension ≤ 4 chars, no spaces).
    /// </summary>
    private static string GetShortFileName(string longName)
    {
        string name = Path.GetFileNameWithoutExtension(longName);
        string ext = Path.GetExtension(longName);

        if (name.Length <= 8 && ext.Length <= 4 && !name.Contains(' '))
            return longName;

        string shortName = name.Replace(" ", string.Empty, StringComparison.Ordinal)
                               .Replace(".", string.Empty, StringComparison.Ordinal);
        if (shortName.Length > 6)
            shortName = string.Concat(shortName.AsSpan(0, 6), "~1");
        string shortExt = ext.Length > 4 ? ext[..4] : ext;

        return string.Concat(shortName, shortExt).ToUpperInvariant();
    }

    private static TableSchema BuildSchema()
    {
        TableId componentTable = WellKnownTableIds.Component;
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            RecipeColumn.String("File", 72),
            RecipeColumn.String("Component_", 72),
            RecipeColumn.Localized("FileName", 255),
            RecipeColumn.Integer("FileSize", 4),
            RecipeColumn.String("Version", 72, nullable: true),
            RecipeColumn.String("Language", 20, nullable: true),
            RecipeColumn.Integer("Attributes", 2, nullable: true),
            RecipeColumn.Integer("Sequence", 2));

        return new TableSchema
        {
            Name = WellKnownTableIds.File,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray.Create(new ForeignKeySpec
            {
                SourceColumn = new ColumnIndex(1),
                TargetTable = componentTable,
            }),
        };
    }
}
