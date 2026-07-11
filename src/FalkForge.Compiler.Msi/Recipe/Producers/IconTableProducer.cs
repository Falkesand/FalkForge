using System.Collections.Immutable;
using System.Security.Cryptography;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>Icon</c> table. Gathers every icon source referenced
/// by the package — shortcut icons (<see cref="ShortcutModel.IconFile"/>),
/// file-association icons (<see cref="FileAssociationModel.IconFile"/>), and the
/// product icon (<see cref="PackageModel.ProductIcon"/>, surfaced as
/// <c>ARPPRODUCTICON</c>) — and emits one <c>Icon</c> row per <b>distinct</b>
/// source, streaming the icon bytes into the <c>Data</c> column the same way
/// <see cref="BinaryTableProducer"/> streams binary payloads.
///
/// <para>
/// Row identity and de-duplication are driven by
/// <see cref="ProducerHelpers.ResolveIconName"/>: two consumers that reference
/// the same source path resolve to the same <c>Icon.Name</c>, so the icon is
/// embedded once and every <c>Shortcut.Icon_</c> / <c>ProgId.Icon_</c> /
/// <c>ARPPRODUCTICON</c> reference points at the shared row. The gather order
/// (shortcuts → file associations → product icon, each in source order) is
/// deterministic so the emitted table is reproducible.
/// </para>
///
/// <para>
/// The table is suppressed entirely when no icons are authored
/// (<see cref="TableSchema.EmitWhenEmpty"/> is <see langword="false"/>), so
/// icon-less packages produce byte-identical output to the pre-Icon pipeline.
/// </para>
/// </summary>
internal sealed class IconTableProducer : ITableProducer
{
    /// <summary>Static schema describing the <c>Icon</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        PackageModel package = context.Resolved.Package;

        ImmutableArray<RecipeRow>.Builder rows = ImmutableArray.CreateBuilder<RecipeRow>();
        // Dedup by resolved Icon.Name: the first consumer of a given source path
        // registers the stream + emits the row; later consumers reuse it.
        HashSet<string> emitted = new(StringComparer.Ordinal);

        // Deterministic gather order: shortcuts, then file associations, then the
        // product icon. Each collection is walked in its authored order.
        foreach (ShortcutModel shortcut in package.Shortcuts)
        {
            Result<Unit> add = TryAddIcon(context, rows, emitted, shortcut.IconFile);
            if (add.IsFailure)
            {
                return Result<ImmutableArray<RecipeRow>>.Failure(add.Error);
            }
        }

        foreach (FileAssociationModel association in package.FileAssociations)
        {
            Result<Unit> add = TryAddIcon(context, rows, emitted, association.IconFile);
            if (add.IsFailure)
            {
                return Result<ImmutableArray<RecipeRow>>.Failure(add.Error);
            }
        }

        Result<Unit> productAdd = TryAddIcon(context, rows, emitted, package.ProductIcon);
        if (productAdd.IsFailure)
        {
            return Result<ImmutableArray<RecipeRow>>.Failure(productAdd.Error);
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static Result<Unit> TryAddIcon(
        RecipeBuildContext context,
        ImmutableArray<RecipeRow>.Builder rows,
        HashSet<string> emitted,
        string? iconFilePath)
    {
        if (string.IsNullOrEmpty(iconFilePath))
        {
            return Unit.Value;
        }

        string iconName = ProducerHelpers.ResolveIconName(iconFilePath);
        if (!emitted.Add(iconName))
        {
            // Already embedded under this name — a distinct consumer sharing the
            // same source path. Skip the duplicate row and stream registration.
            return Unit.Value;
        }

        long length;
        byte[] sha256;
        try
        {
            // Single FileStream pass mirrors BinaryTableProducer: read Length
            // then hash the same stream, avoiding a second kernel open.
            using FileStream fs = File.OpenRead(iconFilePath);
            length = fs.Length;
            sha256 = SHA256.HashData(fs);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Fail loud: a missing/unreadable icon file is an authoring error.
            return Result<Unit>.Failure(
                ErrorKind.FileNotFound,
                $"Icon source file could not be read: '{iconFilePath}'. {ex.Message}");
        }

        StreamSource source = new StreamSource.FilePath(iconFilePath, sha256, length);
        context.Streams.Register(iconName, source);

        rows.Add(new RecipeRow
        {
            Cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(iconName),
                new CellValue.StreamRef(iconName)),
        });

        return Unit.Value;
    }

    private static TableSchema BuildSchema()
    {
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            RecipeColumn.String("Name", 72),
            RecipeColumn.Binary("Data", 0));

        return new TableSchema
        {
            Name = WellKnownTableIds.Icon,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
            // Suppress the table (and its CREATE TABLE) when no icons are
            // authored so icon-less packages stay byte-identical to the
            // pre-Icon pipeline.
            EmitWhenEmpty = false,
        };
    }
}
