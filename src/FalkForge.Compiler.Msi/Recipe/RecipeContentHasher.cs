using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Computes a stable SHA-256 digest over the canonicalised content of an
/// <see cref="MsiDatabaseRecipe"/>. The hash covers <see cref="MsiDatabaseRecipe.Tables"/>,
/// <see cref="MsiDatabaseRecipe.SummaryInfo"/>, <see cref="MsiDatabaseRecipe.Streams"/>,
/// <see cref="MsiDatabaseRecipe.FileSequencing"/>, and
/// <see cref="MsiDatabaseRecipe.CabinetEmbedding"/>. <see cref="MsiDatabaseRecipe.ContentHash"/>
/// itself is intentionally excluded — the recipe being hashed always carries
/// <c>ContentHash = ReadOnlyMemory&lt;byte&gt;.Empty</c> in the hashing payload,
/// so callers may construct the recipe with an empty placeholder and rebuild
/// it with the computed digest via a <c>with</c> expression.
///
/// <para>
/// Determinism rules:
/// <list type="bullet">
///   <item>Tables are hashed in the order supplied (already topological).</item>
///   <item>Streams are sorted by key using <see cref="StringComparer.Ordinal"/> so
///   insertion order into the immutable dictionary is irrelevant.</item>
///   <item>All strings are encoded as length-prefixed UTF-8 (int32 byte count
///   + bytes); empty strings are written as a zero length.</item>
///   <item>Integers are encoded little-endian for portability across host
///   endianness; SHA-256 itself is endianness-agnostic on output but the
///   payload bytes that feed it must be deterministic.</item>
/// </list>
/// </para>
/// </summary>
internal static class RecipeContentHasher
{
    private const byte CellTagNull = 0;
    private const byte CellTagInt = 1;
    private const byte CellTagString = 2;
    private const byte CellTagForeignKey = 3;
    private const byte CellTagStreamRef = 4;

    private const byte StreamTagFilePath = 1;
    private const byte StreamTagInMemory = 2;
    private const byte StreamTagFactory = 3;

    public static ReadOnlyMemory<byte> Compute(MsiDatabaseRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);

        using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        AppendTables(hasher, recipe.Tables);
        AppendSummaryInfo(hasher, recipe.SummaryInfo);
        AppendStreams(hasher, recipe.Streams);
        AppendFileSequencing(hasher, recipe.FileSequencing);
        AppendCabinetEmbedding(hasher, recipe.CabinetEmbedding);

        byte[] digest = hasher.GetHashAndReset();
        return digest;
    }

    private static void AppendTables(IncrementalHash hasher, ImmutableArray<RecipeTable> tables)
    {
        AppendInt32(hasher, tables.IsDefault ? 0 : tables.Length);
        if (tables.IsDefault)
        {
            return;
        }

        foreach (RecipeTable table in tables)
        {
            AppendString(hasher, table.Name.Value);
            AppendColumns(hasher, table.Columns);
            AppendPrimaryKey(hasher, table.PrimaryKey);
            AppendForeignKeys(hasher, table.ForeignKeys);
            AppendRows(hasher, table.Rows);
        }
    }

    private static void AppendColumns(IncrementalHash hasher, ImmutableArray<RecipeColumn> columns)
    {
        AppendInt32(hasher, columns.IsDefault ? 0 : columns.Length);
        if (columns.IsDefault)
        {
            return;
        }

        foreach (RecipeColumn column in columns)
        {
            AppendString(hasher, column.Name);
            AppendInt32(hasher, (int)column.Type);
            AppendInt32(hasher, column.Width);
            AppendBool(hasher, column.Nullable);
            AppendBool(hasher, column.LocalizableKey);
        }
    }

    private static void AppendPrimaryKey(IncrementalHash hasher, ImmutableArray<ColumnIndex> primaryKey)
    {
        AppendInt32(hasher, primaryKey.IsDefault ? 0 : primaryKey.Length);
        if (primaryKey.IsDefault)
        {
            return;
        }

        foreach (ColumnIndex index in primaryKey)
        {
            AppendInt32(hasher, index.Value);
        }
    }

    private static void AppendForeignKeys(IncrementalHash hasher, ImmutableArray<ForeignKeySpec> foreignKeys)
    {
        AppendInt32(hasher, foreignKeys.IsDefault ? 0 : foreignKeys.Length);
        if (foreignKeys.IsDefault)
        {
            return;
        }

        foreach (ForeignKeySpec fk in foreignKeys)
        {
            AppendInt32(hasher, fk.SourceColumn.Value);
            AppendString(hasher, fk.TargetTable.Value);
        }
    }

    private static void AppendRows(IncrementalHash hasher, ImmutableArray<RecipeRow> rows)
    {
        AppendInt32(hasher, rows.IsDefault ? 0 : rows.Length);
        if (rows.IsDefault)
        {
            return;
        }

        foreach (RecipeRow row in rows)
        {
            AppendInt32(hasher, row.Cells.IsDefault ? 0 : row.Cells.Length);
            if (row.Cells.IsDefault)
            {
                continue;
            }

            foreach (CellValue cell in row.Cells)
            {
                AppendCell(hasher, cell);
            }
        }
    }

    private static void AppendCell(IncrementalHash hasher, CellValue cell)
    {
        switch (cell)
        {
            case CellValue.Null:
                AppendByte(hasher, CellTagNull);
                break;
            case CellValue.IntValue i:
                AppendByte(hasher, CellTagInt);
                AppendInt32(hasher, i.Value);
                break;
            case CellValue.StringValue s:
                AppendByte(hasher, CellTagString);
                AppendString(hasher, s.Value);
                break;
            case CellValue.ForeignKey fk:
                AppendByte(hasher, CellTagForeignKey);
                AppendString(hasher, fk.TargetTable.Value);
                AppendString(hasher, fk.TargetKey);
                break;
            case CellValue.StreamRef sr:
                AppendByte(hasher, CellTagStreamRef);
                AppendString(hasher, sr.StreamName);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported CellValue subtype '{cell.GetType().FullName}' encountered while hashing recipe.");
        }
    }

    private static void AppendSummaryInfo(IncrementalHash hasher, SummaryInfoRecipe summaryInfo)
    {
        AppendString(hasher, summaryInfo.Title);
        AppendString(hasher, summaryInfo.Subject);
        AppendString(hasher, summaryInfo.Author);
        AppendString(hasher, summaryInfo.Template);
        AppendString(hasher, summaryInfo.Keywords);
        AppendString(hasher, summaryInfo.Comments);
        AppendString(hasher, summaryInfo.RevisionNumber);
        AppendInt32(hasher, summaryInfo.CodePage);
        AppendString(hasher, summaryInfo.CreatingApplication);
        AppendInt32(hasher, summaryInfo.WordCount);
        AppendInt32(hasher, summaryInfo.PageCount);
        AppendInt32(hasher, summaryInfo.Security);
    }

    private static void AppendStreams(IncrementalHash hasher, ImmutableDictionary<string, StreamSource> streams)
    {
        // Sort by key ordinally so insertion order into the dictionary cannot
        // leak into the hash. ImmutableDictionary enumeration order is an
        // implementation detail we explicitly refuse to depend on.
        IOrderedEnumerable<KeyValuePair<string, StreamSource>> sorted =
            streams.OrderBy(kv => kv.Key, StringComparer.Ordinal);

        // Compute count via Count property (cheap on ImmutableDictionary).
        AppendInt32(hasher, streams.Count);
        foreach (KeyValuePair<string, StreamSource> entry in sorted)
        {
            AppendString(hasher, entry.Key);
            AppendStreamSource(hasher, entry.Value);
        }
    }

    private static void AppendStreamSource(IncrementalHash hasher, StreamSource source)
    {
        // Hash the SHA-256 digest of the stream content rather than the bytes
        // themselves: the digest is precomputed at StreamSource construction
        // and therefore does not require touching the underlying file or
        // factory. This keeps RecipeContentHasher.Compute O(recipe metadata)
        // rather than O(payload bytes).
        ReadOnlyMemory<byte> sha = source.Sha256;
        AppendInt32(hasher, sha.Length);
        hasher.AppendData(sha.Span);
        AppendInt64(hasher, source.Length);

        byte tag = source switch
        {
            StreamSource.FilePath => StreamTagFilePath,
            StreamSource.InMemory => StreamTagInMemory,
            StreamSource.Factory => StreamTagFactory,
            _ => throw new InvalidOperationException(
                $"Unsupported StreamSource subtype '{source.GetType().FullName}' encountered while hashing recipe."),
        };
        AppendByte(hasher, tag);
    }

    private static void AppendFileSequencing(IncrementalHash hasher, ImmutableArray<FileSequenceEntry> sequencing)
    {
        AppendInt32(hasher, sequencing.IsDefault ? 0 : sequencing.Length);
        if (sequencing.IsDefault)
        {
            return;
        }

        foreach (FileSequenceEntry entry in sequencing)
        {
            AppendString(hasher, entry.FileId);
            AppendInt32(hasher, entry.Sequence);
        }
    }

    private static void AppendCabinetEmbedding(IncrementalHash hasher, CabinetEmbedding? embedding)
    {
        if (embedding is null)
        {
            AppendByte(hasher, 0);
            return;
        }

        AppendByte(hasher, 1);
        AppendString(hasher, embedding.StreamName);
    }

    private static void AppendString(IncrementalHash hasher, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            AppendInt32(hasher, 0);
            return;
        }

        int byteCount = Encoding.UTF8.GetByteCount(value);
        AppendInt32(hasher, byteCount);

        // Stack-allocate small strings to avoid heap noise; rent from
        // ArrayPool for larger payloads. 256 bytes covers MSI identifiers
        // (max 31 chars × 4 UTF-8 bytes = 124) and most short strings with
        // headroom.
        if (byteCount <= 256)
        {
            Span<byte> buffer = stackalloc byte[byteCount];
            int written = Encoding.UTF8.GetBytes(value, buffer);
            hasher.AppendData(buffer[..written]);
            return;
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            int written = Encoding.UTF8.GetBytes(value, rented);
            hasher.AppendData(rented, 0, written);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static void AppendInt32(IncrementalHash hasher, int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        hasher.AppendData(buffer);
    }

    private static void AppendInt64(IncrementalHash hasher, long value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        hasher.AppendData(buffer);
    }

    private static void AppendBool(IncrementalHash hasher, bool value)
    {
        Span<byte> buffer = stackalloc byte[1];
        buffer[0] = value ? (byte)1 : (byte)0;
        hasher.AppendData(buffer);
    }

    private static void AppendByte(IncrementalHash hasher, byte value)
    {
        Span<byte> buffer = stackalloc byte[1];
        buffer[0] = value;
        hasher.AppendData(buffer);
    }
}
