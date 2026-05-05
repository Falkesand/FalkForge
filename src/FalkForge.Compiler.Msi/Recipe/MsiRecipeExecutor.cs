using System.Runtime.Versioning;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Phase 7 Windows-only executor that walks an immutable
/// <see cref="MsiDatabaseRecipe"/> and applies it to a real
/// <c>msi.dll</c>-backed <see cref="MsiDatabase"/>. The executor is a thin
/// translator: it issues the recipe-supplied <c>CREATE TABLE</c> SQL,
/// inserts each row using the recipe's <c>InsertViewSql</c>, writes summary
/// information from <see cref="MsiDatabaseRecipe.SummaryInfo"/>, registers
/// any <see cref="MsiDatabaseRecipe.Streams"/> entries plus an optional
/// <see cref="MsiDatabaseRecipe.CabinetEmbeddings"/>, and finally calls
/// <see cref="MsiDatabase.Commit"/>. All decisions live in the recipe; the
/// executor never inspects <see cref="FalkForge.Models.ResolvedPackage"/> or
/// <see cref="FalkForge.Models.PackageModel"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MsiRecipeExecutor
{
    private readonly MsiDatabase _database;

    public MsiRecipeExecutor(MsiDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    /// <summary>
    /// Walks <paramref name="recipe"/> in declared order, applies every
    /// table, summary-info field, and stream to the wrapped
    /// <see cref="MsiDatabase"/>, then commits. Returns the first failure
    /// encountered with a message naming the failing table or stream so
    /// callers can locate the offending recipe input.
    /// </summary>
    public Result<Unit> Apply(MsiDatabaseRecipe recipe)
    {
        if (recipe is null)
        {
            return Result<Unit>.Failure(
                ErrorKind.Validation,
                "Recipe cannot be null.");
        }

        // Two-phase table application mirrors legacy TableEmitter's operation order:
        // Phase 1 — CREATE TABLE for every BUILT-IN table (matches TableEmitter.CreateTables batch).
        // Phase 2 — INSERT rows for every BUILT-IN table (matches TableEmitter.Emit* methods).
        // Separating CREATE from INSERT for built-in tables ensures the OLE compound document's
        // internal page allocation follows the same sequence as legacy, producing byte-identical MSI.
        //
        // Non-built-in tables (IsBuiltIn = false — custom user tables from CustomTablesProducer)
        // are applied single-phase (CREATE then INSERT immediately) AFTER the two-phase built-in
        // pass. This avoids an msi.dll CREATE TABLE error (1615) that occurs when a user-table
        // column name coincides with a previously-created built-in table name (e.g., a column
        // named "Environment" in a custom table after the Environment built-in table is created
        // but before it has rows). Single-phase for these tables matches legacy TableEmitter which
        // applied EmitCustomTables after all built-in table emissions were complete.
        foreach (RecipeTable table in recipe.Tables)
        {
            if (!table.IsBuiltIn) continue;
            Result<Unit> createResult = CreateTable(table);
            if (createResult.IsFailure)
            {
                return createResult;
            }
        }

        foreach (RecipeTable table in recipe.Tables)
        {
            if (!table.IsBuiltIn) continue;
            Result<Unit> insertResult = InsertTableRows(table, recipe.Streams);
            if (insertResult.IsFailure)
            {
                return insertResult;
            }
        }

        // Single-phase for non-built-in tables: CREATE then INSERT per table, appended after
        // all built-in table operations. Matches legacy EmitCustomTables order.
        foreach (RecipeTable table in recipe.Tables)
        {
            if (table.IsBuiltIn) continue;
            Result<Unit> tableResult = ApplyTable(table, recipe.Streams);
            if (tableResult.IsFailure)
            {
                return tableResult;
            }
        }

        // Cabinets are embedded before SummaryInfo, matching the legacy compiler's
        // step order: (1) emit tables, (2) embed cabs, (3) set summary info, (4) commit.
        Result<Unit> cabinetResult = ApplyCabinetEmbeddings(recipe.CabinetEmbeddings);
        if (cabinetResult.IsFailure)
        {
            return cabinetResult;
        }

        Result<Unit> streamsResult = ApplyExtraStreams(recipe);
        if (streamsResult.IsFailure)
        {
            return streamsResult;
        }

        Result<Unit> summaryResult = ApplySummaryInfo(recipe.SummaryInfo);
        if (summaryResult.IsFailure)
        {
            return summaryResult;
        }

        return _database.Commit();
    }

    private Result<Unit> CreateTable(RecipeTable table)
    {
        Result<Unit> createResult = _database.Execute(table.CreateTableSql);
        if (createResult.IsFailure)
        {
            return Result<Unit>.Failure(
                ErrorKind.CompilationError,
                $"msi.dll error during {table.Name.Value} CREATE TABLE: {createResult.Error.Message}");
        }

        return Unit.Value;
    }

    private Result<Unit> InsertTableRows(
        RecipeTable table,
        System.Collections.Immutable.ImmutableDictionary<string, StreamSource> streams)
    {
        for (int rowIndex = 0; rowIndex < table.Rows.Length; rowIndex++)
        {
            RecipeRow row = table.Rows[rowIndex];
            Result<Unit> insertResult = InsertRow(table, row, streams, rowIndex);
            if (insertResult.IsFailure)
            {
                return insertResult;
            }
        }

        return Unit.Value;
    }

    // Kept for backward compatibility with callers that may invoke ApplyTable directly.
    // Internal use; the primary Apply() path now calls CreateTable + InsertTableRows
    // separately to match the two-phase write order of the legacy TableEmitter.
    private Result<Unit> ApplyTable(
        RecipeTable table,
        System.Collections.Immutable.ImmutableDictionary<string, StreamSource> streams)
    {
        Result<Unit> createResult = CreateTable(table);
        if (createResult.IsFailure)
        {
            return createResult;
        }

        return InsertTableRows(table, streams);
    }

    private Result<Unit> InsertRow(
        RecipeTable table,
        RecipeRow row,
        System.Collections.Immutable.ImmutableDictionary<string, StreamSource> streams,
        int rowIndex)
    {
        // Track temporary spill files for in-memory or factory-backed streams
        // so they can be deleted after the insert. msi.dll's MsiRecordSetStream
        // copies bytes into the database during MsiViewModify(Insert), so it is
        // safe to delete the spill files after the insert returns.
        List<string>? spillFiles = null;
        try
        {
            try
            {
                Result<Unit> insertResult = _database.InsertRow(
                    table.InsertViewSql,
                    record =>
                    {
                        for (int i = 0; i < row.Cells.Length; i++)
                        {
                            uint field = (uint)(i + 1);
                            CellValue cell = row.Cells[i];
                            switch (cell)
                            {
                                case CellValue.Null:
                                    // Leave field unset (msi.dll treats absent fields as NULL).
                                    break;
                                case CellValue.IntValue iv:
                                    record.SetInteger(field, iv.Value);
                                    break;
                                case CellValue.StringValue sv:
                                    record.SetString(field, sv.Value);
                                    break;
                                case CellValue.ForeignKey fk:
                                    record.SetString(field, fk.TargetKey);
                                    break;
                                case CellValue.StreamRef sr:
                                    string streamPath = MaterializeStreamToFile(sr.StreamName, streams, ref spillFiles);
                                    record.SetStream(field, streamPath);
                                    break;
                                default:
                                    throw new InvalidOperationException(
                                        $"Unsupported CellValue subtype '{cell.GetType().Name}'.");
                            }
                        }
                    });

                if (insertResult.IsFailure)
                {
                    return Result<Unit>.Failure(
                        ErrorKind.CompilationError,
                        $"msi.dll error during {table.Name.Value} insert (row {rowIndex}): {insertResult.Error.Message}");
                }

                return Unit.Value;
            }
            catch (InvalidOperationException ex)
            {
                // MsiRecord.Set* throws InvalidOperationException on msi.dll
                // failure; surface it as a CompilationError so callers see a
                // unified failure shape.
                return Result<Unit>.Failure(
                    ErrorKind.CompilationError,
                    $"msi.dll error during {table.Name.Value} insert (row {rowIndex}): {ex.Message}");
            }
        }
        finally
        {
            DeleteSpillFiles(spillFiles);
        }
    }

    private Result<Unit> ApplySummaryInfo(SummaryInfoRecipe summary)
    {
        // Property order matches MsiCompiler (legacy) exactly so that
        // MsiSummaryInfoSetProperty writes properties to the OLE PropertySetStorage
        // in the same sequence — this affects the binary layout of the
        // SummaryInformation stream and is required for byte-identical output.
        return _database.SetSummaryInfo(writer =>
        {
            writer
                .Title(summary.Title)
                .Subject(summary.Subject)
                .Author(summary.Author)
                .Keywords(summary.Keywords)
                .Comments(summary.Comments)
                .Template(summary.Template)
                .RevisionNumber(summary.RevisionNumber)
                .CreatingApplication(summary.CreatingApplication)
                .WordCount(summary.WordCount)
                .PageCount(summary.PageCount)
                .Security(summary.Security)
                .Codepage(summary.CodePage);
        });
    }

    private Result<Unit> ApplyExtraStreams(MsiDatabaseRecipe recipe)
    {
        if (recipe.Streams.IsEmpty)
        {
            return Unit.Value;
        }

        // Discover which stream names were already consumed inline by
        // CellValue.StreamRef cells so we do not double-register them via
        // _Streams. Streams referenced from cells live in the table's stream
        // column directly; only "free" streams (referenced solely from
        // recipe.Streams) need an explicit _Streams insert.
        HashSet<string> referencedByCells = new(StringComparer.Ordinal);
        foreach (RecipeTable table in recipe.Tables)
        {
            foreach (RecipeRow row in table.Rows)
            {
                foreach (CellValue cell in row.Cells)
                {
                    if (cell is CellValue.StreamRef sr)
                    {
                        referencedByCells.Add(sr.StreamName);
                    }
                }
            }
        }

        bool ensuredTable = false;
        foreach (KeyValuePair<string, StreamSource> entry in recipe.Streams)
        {
            if (referencedByCells.Contains(entry.Key))
            {
                continue;
            }

            if (!ensuredTable)
            {
                Result<Unit> ensureResult = EnsureStreamsTable();
                if (ensureResult.IsFailure)
                {
                    return ensureResult;
                }

                ensuredTable = true;
            }

            Result<Unit> writeResult = WriteStreamsRow(entry.Key, entry.Value);
            if (writeResult.IsFailure)
            {
                return writeResult;
            }
        }

        return Unit.Value;
    }

    private Result<Unit> ApplyCabinetEmbeddings(
        System.Collections.Immutable.ImmutableArray<CabinetEmbedding> embeddings)
    {
        if (embeddings.IsDefaultOrEmpty)
        {
            return Unit.Value;
        }

        Result<Unit> ensureResult = EnsureStreamsTable();
        if (ensureResult.IsFailure)
        {
            return ensureResult;
        }

        foreach (CabinetEmbedding embedding in embeddings)
        {
            Result<Unit> writeResult = WriteStreamsRow(embedding.StreamName, embedding.Source);
            if (writeResult.IsFailure)
            {
                return writeResult;
            }
        }

        return Unit.Value;
    }

    private Result<Unit> EnsureStreamsTable()
    {
        // The _Streams table is special and may already exist if a producer
        // registered streams as a regular table. Best-effort CREATE; ignore
        // failure since a duplicate-table error is benign here.
        _database.Execute(
            "CREATE TABLE `_Streams` (`Name` CHAR(72) NOT NULL, `Data` OBJECT NOT NULL PRIMARY KEY `Name`)");
        return Unit.Value;
    }

    private Result<Unit> WriteStreamsRow(string streamName, StreamSource source)
    {
        string spillPath = WriteStreamToTempFile(source);
        try
        {
            Result<Unit> insertResult = _database.InsertRow(
                "SELECT `Name`, `Data` FROM `_Streams`",
                record => record
                    .SetString(1, streamName)
                    .SetStream(2, spillPath));

            if (insertResult.IsFailure)
            {
                return Result<Unit>.Failure(
                    ErrorKind.CompilationError,
                    $"msi.dll error registering stream '{streamName}': {insertResult.Error.Message}");
            }

            return Unit.Value;
        }
        catch (InvalidOperationException ex)
        {
            return Result<Unit>.Failure(
                ErrorKind.CompilationError,
                $"msi.dll error registering stream '{streamName}': {ex.Message}");
        }
        finally
        {
            DeleteSpillFile(spillPath);
        }
    }

    private static string MaterializeStreamToFile(
        string streamName,
        System.Collections.Immutable.ImmutableDictionary<string, StreamSource> streams,
        ref List<string>? spillFiles)
    {
        if (!streams.TryGetValue(streamName, out StreamSource? source))
        {
            throw new InvalidOperationException(
                $"Stream '{streamName}' referenced by a CellValue.StreamRef but not present in recipe.Streams.");
        }

        // FilePath sources can be passed directly to MsiRecordSetStream; only
        // in-memory and factory sources require a spill.
        if (source is StreamSource.FilePath filePath)
        {
            return filePath.Path;
        }

        string tempPath = WriteStreamToTempFile(source);
        spillFiles ??= new List<string>();
        spillFiles.Add(tempPath);
        return tempPath;
    }

    private static string WriteStreamToTempFile(StreamSource source)
    {
        if (source is StreamSource.FilePath filePath)
        {
            return filePath.Path;
        }

        string tempPath = Path.Combine(
            Path.GetTempPath(),
            $"falkforge-recipe-stream-{Guid.NewGuid():N}.bin");

        using (Stream input = source.Open())
        using (FileStream output = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            input.CopyTo(output);
        }

        return tempPath;
    }

    private static void DeleteSpillFiles(List<string>? spillFiles)
    {
        if (spillFiles is null)
        {
            return;
        }

        foreach (string path in spillFiles)
        {
            DeleteSpillFile(path);
        }
    }

    private static void DeleteSpillFile(string path)
    {
        try
        {
            if (File.Exists(path) && IsTempSpillFile(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup. msi.dll occasionally retains a brief
            // handle on the source file; the file lives in the system temp
            // directory and will be reclaimed by routine cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Same rationale as IOException above.
        }
    }

    private static bool IsTempSpillFile(string path)
    {
        // Defense-in-depth: only ever delete files that look like our own
        // spills (located in temp and prefixed with our marker). Caller
        // contract already guarantees this, but the explicit check protects
        // against future refactors that might pass a caller-owned path.
        string fileName = Path.GetFileName(path);
        return fileName.StartsWith("falkforge-recipe-stream-", StringComparison.Ordinal);
    }
}
