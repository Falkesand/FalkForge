using System.Collections.Immutable;
using System.Text;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Compiler.Msi.Tables;
using FalkForge.Extensibility;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Pure function that turns a <see cref="ResolvedPackage"/> plus any extension
/// table contributors into an immutable <see cref="MsiDatabaseRecipe"/>.
///
/// Phase 4 wires in the first batch of built-in producers (Property, Directory,
/// Component, File, Feature). Each producer emits one <see cref="RecipeTable"/>
/// — even when the source data is empty — so downstream phases can rely on a
/// stable table set. Pruning of empty tables is deliberately deferred.
/// </summary>
public static class MsiRecipeBuilder
{
    /// <summary>
    /// Build a recipe from the resolved package, extension contributors, and
    /// build options. Returns <see cref="ErrorKind.Validation"/> failure for
    /// any null argument; otherwise runs the built-in producer pipeline in a
    /// fixed order and aggregates the resulting tables.
    /// </summary>
    public static Result<MsiDatabaseRecipe> Build(
        ResolvedPackage resolved,
        IReadOnlyList<IMsiTableContributor> contributors,
        MsiRecipeBuildOptions options)
    {
        if (resolved is null)
        {
            return Result<MsiDatabaseRecipe>.Failure(
                ErrorKind.Validation,
                "Resolved package cannot be null.");
        }

        if (contributors is null)
        {
            return Result<MsiDatabaseRecipe>.Failure(
                ErrorKind.Validation,
                "Contributors cannot be null.");
        }

        if (options is null)
        {
            return Result<MsiDatabaseRecipe>.Failure(
                ErrorKind.Validation,
                "Options cannot be null.");
        }

        RecipeBuildContext context = new(
            resolved,
            options,
            new NoOpFileSequencer(),
            new DictionaryStreamRegistry());

        // Fixed producer order. The order matches the natural foreign-key
        // dependency direction (Directory before Component, Component before
        // File, Feature last) so future cross-producer FK validators can rely
        // on referenced tables being present in BuiltTables.
        ITableProducer[] producers =
        {
            new PropertyTableProducer(),
            new DirectoryTableProducer(),
            new ComponentTableProducer(),
            new FileTableProducer(),
            new FeatureTableProducer(),
        };

        ImmutableArray<RecipeTable>.Builder tableBuilder = ImmutableArray.CreateBuilder<RecipeTable>(producers.Length);
        foreach (ITableProducer producer in producers)
        {
            Result<ImmutableArray<RecipeRow>> producerResult = producer.Produce(context);
            if (producerResult.IsFailure)
            {
                return Result<MsiDatabaseRecipe>.Failure(producerResult.Error);
            }

            ImmutableArray<RecipeRow> rows = producerResult.Value;
            context.AddBuiltTable(producer.Schema.Name, rows);

            RecipeTable table = new()
            {
                Name = producer.Schema.Name,
                Columns = producer.Schema.Columns,
                Rows = rows,
                PrimaryKey = producer.Schema.PrimaryKey,
                CreateTableSql = LookupCreateTableSql(producer.Schema.Name),
                InsertViewSql = BuildInsertViewSql(producer.Schema),
            };
            tableBuilder.Add(table);
        }

        SummaryInfoRecipe summaryInfo = new()
        {
            Title = string.Empty,
            Subject = string.Empty,
            Author = string.Empty,
            Template = string.Empty,
            Keywords = string.Empty,
            Comments = string.Empty,
            RevisionNumber = 0,
            CodePage = 1252,
        };

        MsiDatabaseRecipe recipe = new()
        {
            Tables = tableBuilder.MoveToImmutable(),
            SummaryInfo = summaryInfo,
            Streams = ImmutableDictionary<string, StreamSource>.Empty,
            FileSequencing = ImmutableArray<FileSequenceEntry>.Empty,
            CabinetEmbedding = null,
            ContentHash = ReadOnlyMemory<byte>.Empty,
        };

        return Result<MsiDatabaseRecipe>.Success(recipe);
    }

    private static string LookupCreateTableSql(TableId table)
    {
        // Hard-wired lookup against MsiTableDefinitions. Phase 4 ships only the
        // five tables emitted by the first producer batch; later phases will
        // either extend this lookup or migrate to a contributor-driven map.
        return table.Value switch
        {
            "Property" => MsiTableDefinitions.CreatePropertyTable,
            "Directory" => MsiTableDefinitions.CreateDirectoryTable,
            "Component" => MsiTableDefinitions.CreateComponentTable,
            "File" => MsiTableDefinitions.CreateFileTable,
            "Feature" => MsiTableDefinitions.CreateFeatureTable,
            _ => throw new InvalidOperationException(
                $"No CREATE TABLE SQL registered for table '{table.Value}'."),
        };
    }

    private static string BuildInsertViewSql(TableSchema schema)
    {
        // Mirrors the SQL string TableEmitter passes to MsiDatabase.InsertRow:
        //   "SELECT `c1`, `c2` FROM `Table`"
        // Identifiers are validated at TableId/RecipeColumn construction so
        // direct interpolation is safe.
        StringBuilder builder = new("SELECT ");
        ImmutableArray<RecipeColumn> columns = schema.Columns;
        for (int i = 0; i < columns.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append('`').Append(columns[i].Name).Append('`');
        }

        builder.Append(" FROM `").Append(schema.Name.Value).Append('`');
        return builder.ToString();
    }
}
