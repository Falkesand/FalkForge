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
/// Phase 4 wires in the built-in producers (Property, Directory, Feature,
/// Component, File, FeatureComponents, FeatureCondition, Upgrade, Media,
/// Registry, RemoveRegistry, ServiceInstall, ServiceControl, Shortcut,
/// Environment, Font, LaunchCondition, IniFile, CreateFolder, DuplicateFile,
/// CustomAction, LockPermissions, MsiLockPermissionsEx, MIME, ProgId,
/// Extension, Verb, MoveFile, RemoveFile). Each producer emits one
/// <see cref="RecipeTable"/> — even when the source data is empty — so
/// downstream phases can rely on a stable table set. Pruning of empty
/// tables is deliberately deferred.
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
        // dependency direction so future cross-producer FK validators can
        // rely on referenced tables being present in BuiltTables. Feature
        // is emitted before Component so the FeatureComponents junction can
        // see both parent tables; FeatureCondition (Condition table) sits
        // immediately after Feature because it FKs back to Feature; Upgrade
        // has no FK dependencies and is emitted before any Component-bound
        // producer for grouping; Environment/MoveFile/RemoveFile all
        // reference Component (and Directory in MoveFile/RemoveFile cases)
        // and therefore follow the parent producers.
        ITableProducer[] producers =
        {
            new PropertyTableProducer(),
            new DirectoryTableProducer(),
            new FeatureTableProducer(),
            new ComponentTableProducer(),
            new FileTableProducer(),
            new FeatureComponentsTableProducer(),
            new FeatureConditionTableProducer(),
            new UpgradeTableProducer(),
            new MediaTableProducer(),
            new RegistryTableProducer(),
            new RemoveRegistryTableProducer(),
            new ServiceInstallTableProducer(),
            new ServiceControlTableProducer(),
            new ShortcutTableProducer(),
            new EnvironmentTableProducer(),
            new FontTableProducer(),
            new LaunchConditionTableProducer(),
            new IniFileTableProducer(),
            new CreateFolderTableProducer(),
            new DuplicateFileTableProducer(),
            new CustomActionTableProducer(),
            new LockPermissionsTableProducer(),
            new MsiLockPermissionsExTableProducer(),
            new MIMETableProducer(),
            new ProgIdTableProducer(),
            new ExtensionTableProducer(),
            new VerbTableProducer(),
            new MoveFileTableProducer(),
            new RemoveFileTableProducer(),
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
                ForeignKeys = producer.Schema.ForeignKeys,
            };
            tableBuilder.Add(table);
        }

        // Phase 5: run recipe-level validators after every producer has
        // emitted rows. Both validators are pure functions over the
        // already-built tables, so failure here unambiguously indicates a
        // producer-level bug or a malformed input package — never a transient
        // condition that retrying would fix.
        ImmutableArray<RecipeTable> validatedTables = tableBuilder.ToImmutable();
        Result<Unit> pkResult = PrimaryKeyValidator.Validate(validatedTables);
        if (pkResult.IsFailure)
        {
            return Result<MsiDatabaseRecipe>.Failure(pkResult.Error);
        }

        Result<Unit> fkResult = ForeignKeyValidator.Validate(validatedTables);
        if (fkResult.IsFailure)
        {
            return Result<MsiDatabaseRecipe>.Failure(fkResult.Error);
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

        // Construct the recipe with an empty ContentHash placeholder, then
        // rebuild it via a with-expression carrying the digest. The hashing
        // payload deliberately excludes ContentHash itself, so the placeholder
        // never affects the output digest.
        MsiDatabaseRecipe recipe = new()
        {
            Tables = validatedTables,
            SummaryInfo = summaryInfo,
            Streams = ImmutableDictionary<string, StreamSource>.Empty,
            FileSequencing = ImmutableArray<FileSequenceEntry>.Empty,
            CabinetEmbedding = null,
            ContentHash = ReadOnlyMemory<byte>.Empty,
        };

        recipe = recipe with { ContentHash = RecipeContentHasher.Compute(recipe) };
        return Result<MsiDatabaseRecipe>.Success(recipe);
    }

    private static string LookupCreateTableSql(TableId table)
    {
        // Hard-wired lookup against MsiTableDefinitions. Each producer
        // registered in the pipeline above must have its CREATE TABLE SQL
        // wired here; later phases will either extend this lookup or migrate
        // to a contributor-driven map.
        return table.Value switch
        {
            "Property" => MsiTableDefinitions.CreatePropertyTable,
            "Directory" => MsiTableDefinitions.CreateDirectoryTable,
            "Component" => MsiTableDefinitions.CreateComponentTable,
            "File" => MsiTableDefinitions.CreateFileTable,
            "Feature" => MsiTableDefinitions.CreateFeatureTable,
            "FeatureComponents" => MsiTableDefinitions.CreateFeatureComponentsTable,
            "Condition" => MsiTableDefinitions.CreateConditionTable,
            "Upgrade" => MsiTableDefinitions.CreateUpgradeTable,
            "Media" => MsiTableDefinitions.CreateMediaTable,
            "Registry" => MsiTableDefinitions.CreateRegistryTable,
            "RemoveRegistry" => MsiTableDefinitions.CreateRemoveRegistryTable,
            "ServiceInstall" => MsiTableDefinitions.CreateServiceInstallTable,
            "ServiceControl" => MsiTableDefinitions.CreateServiceControlTable,
            "Shortcut" => MsiTableDefinitions.CreateShortcutTable,
            "Environment" => MsiTableDefinitions.CreateEnvironmentTable,
            "Font" => MsiTableDefinitions.CreateFontTable,
            "LaunchCondition" => MsiTableDefinitions.CreateLaunchConditionTable,
            "IniFile" => MsiTableDefinitions.CreateIniFileTable,
            "CreateFolder" => MsiTableDefinitions.CreateCreateFolderTable,
            "DuplicateFile" => MsiTableDefinitions.CreateDuplicateFileTable,
            "CustomAction" => MsiTableDefinitions.CreateCustomActionTable,
            "LockPermissions" => MsiTableDefinitions.CreateLockPermissionsTable,
            "MsiLockPermissionsEx" => MsiTableDefinitions.CreateMsiLockPermissionsExTable,
            "MIME" => MsiTableDefinitions.CreateMimeTable,
            "ProgId" => MsiTableDefinitions.CreateProgIdTable,
            "Extension" => MsiTableDefinitions.CreateExtensionTable,
            "Verb" => MsiTableDefinitions.CreateVerbTable,
            "MoveFile" => MsiTableDefinitions.CreateMoveFileTable,
            "RemoveFile" => MsiTableDefinitions.CreateRemoveFileTable,
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
