using System.Collections.Immutable;
using System.Security.Cryptography;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>Binary</c> table. Walks
/// <see cref="PackageModel.Binaries"/> and emits one row per entry,
/// mirroring the legacy <see cref="Tables.TableEmitter"/>'s
/// <c>EmitBinaries</c>: <c>Name</c> from <see cref="BinaryModel.Name"/>,
/// <c>Data</c> as a <see cref="CellValue.StreamRef"/> keyed by the binary
/// name.
///
/// Stream registration: the producer calls
/// <see cref="IStreamRegistry.Register"/> on <see cref="RecipeBuildContext.Streams"/>
/// for each binary, supplying a <see cref="StreamSource.FilePath"/> that
/// wraps the <see cref="BinaryModel.SourcePath"/>. SHA-256 and length are
/// computed at produce-time so the recipe is self-consistent and
/// reproducibility hashing does not re-read files. The executor writes the
/// stream bytes into the MSI via <c>MsiRecord.SetStream</c>.
///
/// Security: <see cref="BinaryModel.SourcePath"/> is taken verbatim from the
/// validated domain model; no additional path-traversal normalization is
/// applied here because the upstream <see cref="Core.Validation"/> layer
/// already rejects paths that escape the project root. Callers that inject
/// <c>BinaryModel</c> from untrusted sources must validate source paths
/// before reaching this producer.
/// </summary>
internal sealed class BinaryTableProducer : ITableProducer
{
    /// <summary>Static schema describing the <c>Binary</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IReadOnlyList<BinaryModel> binaries = context.Resolved.Package.Binaries;

        if (binaries.Count == 0)
        {
            return Result<ImmutableArray<RecipeRow>>.Success(ImmutableArray<RecipeRow>.Empty);
        }

        ImmutableArray<RecipeRow>.Builder rows =
            ImmutableArray.CreateBuilder<RecipeRow>(binaries.Count);

        foreach (BinaryModel binary in binaries)
        {
            // Read file metadata once; bytes stay on disk until the executor
            // calls StreamSource.Open(). SHA-256 is computed here so the
            // recipe content hash covers binary payloads without re-reading.
            FileInfo info = new(binary.SourcePath);
            byte[] sha256 = ComputeFileSha256(binary.SourcePath);

            StreamSource source = new StreamSource.FilePath(
                binary.SourcePath,
                sha256,
                info.Length);

            // Register stream into the shared registry. DictionaryStreamRegistry
            // throws on duplicate names — a duplicate in Binaries is a producer
            // input bug surfaced early.
            context.Streams.Register(binary.Name, source);

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(binary.Name),
                new CellValue.StreamRef(binary.Name));

            rows.Add(new RecipeRow { Cells = cells });
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static byte[] ComputeFileSha256(string path)
    {
        using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return SHA256.HashData(fs);
    }

    private static TableSchema BuildSchema()
    {
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            new RecipeColumn
            {
                Name = "Name",
                Type = ColumnType.String,
                Width = 72,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Data",
                Type = ColumnType.Binary,
                Width = 0,
                Nullable = false,
                LocalizableKey = false,
            });

        return new TableSchema
        {
            Name = TableId.Create("Binary").Value,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };
    }
}
