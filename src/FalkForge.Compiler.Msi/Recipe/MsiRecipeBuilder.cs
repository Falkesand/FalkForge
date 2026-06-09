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
/// Environment, Font, LaunchCondition, IniFile, RemoveIniFile, CreateFolder,
/// DuplicateFile, Binary, CustomAction, LockPermissions*, MsiLockPermissionsEx*,
/// MIME, ProgId, Extension, Class, TypeLib, MsiAssembly, MsiAssemblyName, Verb,
/// MoveFile, RemoveFile, InstallUISequence, InstallExecuteSequence).
/// Most producers emit one <see cref="RecipeTable"/> even when the source data
/// is empty. Producers whose <see cref="TableSchema.EmitWhenEmpty"/> is
/// <see langword="false"/> are suppressed from the recipe when they return zero
/// rows — parity with the legacy <see cref="Tables.TableEmitter"/> which gates
/// certain CREATE TABLE statements on the presence of matching data (marked *
/// above).
/// </summary>
public static class MsiRecipeBuilder
{
    /// <summary>
    /// Build a recipe from the resolved package, extension contributors, and
    /// build options. Returns <see cref="ErrorKind.Validation"/> failure for
    /// any null argument; otherwise runs the built-in producer pipeline in a
    /// fixed order and aggregates the resulting tables. Multi-table producers
    /// (e.g. <c>CustomTablesProducer</c>) are appended after the fixed pipeline
    /// and after primary-key / foreign-key validation of the built-in tables.
    /// </summary>
    internal static Result<MsiDatabaseRecipe> Build(
        ResolvedPackage resolved,
        IReadOnlyList<IMsiTableContributor> contributors,
        MsiRecipeBuildOptions options,
        IReadOnlyList<IMultiTableProducer> multiProducers)
    {
        if (multiProducers is null)
        {
            return Result<MsiDatabaseRecipe>.Failure(
                ErrorKind.Validation,
                "Multi-table producers list cannot be null.");
        }

        return BuildCore(resolved, contributors, options, multiProducers);
    }

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
        => BuildCore(resolved, contributors, options, []);

    private static Result<MsiDatabaseRecipe> BuildCore(
        ResolvedPackage resolved,
        IReadOnlyList<IMsiTableContributor> contributors,
        MsiRecipeBuildOptions options,
        IReadOnlyList<IMultiTableProducer> multiProducers)
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

        // Fixed producer order. Order matches legacy TableEmitter.CreateTables exactly so that
        // the two-phase executor (CREATE all → INSERT all) writes tables in the same sequence
        // as legacy, producing byte-identical OLE compound document page allocation.
        // Legacy order: Directory → Component → File → Feature → FeatureComponents → Media →
        //   Property → Registry → Shortcut → ServiceInstall → ServiceControl → Upgrade →
        //   LaunchCondition → InstallExecuteSequence → InstallUISequence → Environment →
        //   Font → IniFile → RemoveIniFile → Extension → Verb → MIME → ProgId →
        //   CustomAction → Binary → RemoveRegistry → RemoveFile → CreateFolder →
        //   MoveFile → DuplicateFile → MsiAssembly → MsiAssemblyName → Condition →
        //   Class → TypeLib → [LockPermissions] → [MsiLockPermissionsEx]
        // All producers whose tables appear unconditionally in legacy are listed in the
        // same order; LockPermissions and MsiLockPermissionsEx are at the end with
        // EmitWhenEmpty=false so they are suppressed when no permission entries exist.
        ITableProducer[] producers =
        {
            new DirectoryTableProducer(),
            new ComponentTableProducer(),
            new FileTableProducer(),
            new FeatureTableProducer(),
            new FeatureComponentsTableProducer(),
            new MediaTableProducer(),
            new PropertyTableProducer(),
            new RegistryTableProducer(),
            new ShortcutTableProducer(),
            new ServiceInstallTableProducer(),
            new ServiceControlTableProducer(),
            new UpgradeTableProducer(),
            new LaunchConditionTableProducer(),
            new InstallExecuteSequenceTableProducer(),
            new InstallUISequenceTableProducer(),
            new EnvironmentTableProducer(),
            new FontTableProducer(),
            new IniFileTableProducer(),
            new RemoveIniFileTableProducer(),
            new ExtensionTableProducer(),
            new VerbTableProducer(),
            new MIMETableProducer(),
            new ProgIdTableProducer(),
            new CustomActionTableProducer(),
            new BinaryTableProducer(),
            new RemoveRegistryTableProducer(),
            new RemoveFileTableProducer(),
            new CreateFolderTableProducer(),
            new MoveFileTableProducer(),
            new DuplicateFileTableProducer(),
            new MsiAssemblyTableProducer(),
            new MsiAssemblyNameTableProducer(),
            new FeatureConditionTableProducer(),
            new ClassTableProducer(),
            new TypeLibTableProducer(),
            new LockPermissionsTableProducer(),
            new MsiLockPermissionsExTableProducer(),
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

            // Honour the producer's EmitWhenEmpty flag: when false and the
            // producer returned zero rows, suppress both the RecipeTable entry
            // and the CREATE TABLE statement. This mirrors legacy TableEmitter
            // behaviour for tables such as LockPermissions and
            // MsiLockPermissionsEx which are only created when at least one
            // matching permission entry is present.
            if (!producer.Schema.EmitWhenEmpty && rows.IsEmpty)
            {
                continue;
            }

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

        // Phase 5b: run multi-table producers. These emit dynamic-schema tables
        // (e.g. user-defined custom tables) and are appended after the built-in
        // pipeline. They are intentionally excluded from PK/FK validation because
        // their schemas are not known at compile time and have no FK relationships
        // to the fixed built-in tables.
        //
        // FK validation gap — by design: PrimaryKeyValidator and ForeignKeyValidator
        // run only over the fixed built-in tables (validatedTables) above. Tables
        // emitted here are NOT checked. Each IMultiTableProducer implementation is
        // solely responsible for FK integrity within its tables and against the
        // built-in tables. See IMultiTableProducer XML doc for the full contract.
        ImmutableArray<RecipeTable> finalTables;
        if (multiProducers.Count == 0)
        {
            finalTables = validatedTables;
        }
        else
        {
            // Pre-size: known fixed count plus a reasonable estimate for dynamic tables.
            ImmutableArray<RecipeTable>.Builder multiBuilder =
                ImmutableArray.CreateBuilder<RecipeTable>(validatedTables.Length + multiProducers.Count);
            multiBuilder.AddRange(validatedTables);

            foreach (IMultiTableProducer multiProducer in multiProducers)
            {
                Result<ImmutableArray<RecipeTable>> multiResult = multiProducer.Produce(context);
                if (multiResult.IsFailure)
                {
                    return Result<MsiDatabaseRecipe>.Failure(multiResult.Error);
                }

                multiBuilder.AddRange(multiResult.Value);
            }

            finalTables = multiBuilder.ToImmutable();
        }

        var pkg = resolved.Package;

        // PID_REVNUMBER is the MSI PackageCode — must be unique per distinct package
        // byte sequence (SECREPAIR / KB2918614). Resolution order:
        //   1. Explicit PackageCode on the model (rare — pinned re-releases only).
        //   2. Reproducible mode → content digest via PackageCodeDerivation.Derive().
        //   3. Normal mode (null PackageCode) → derive from content + ResolvedPackage.InstanceId.
        //      InstanceId is a per-instance Guid assigned at ResolvedPackage construction,
        //      so two separate packaging events (different ResolvedPackage objects) produce
        //      different PackageCodes even with identical content, while multiple
        //      MsiRecipeBuilder.Build() calls on the *same* instance remain stable.
        Guid packageCode;
        if (pkg.PackageCode.HasValue)
        {
            packageCode = pkg.PackageCode.Value;
        }
        else
        {
            var deriveResult = PackageCodeDerivation.Derive(resolved);
            if (deriveResult.IsFailure)
                return Result<MsiDatabaseRecipe>.Failure(deriveResult.Error);
            packageCode = deriveResult.Value;
        }

        SummaryInfoRecipe summaryInfo = new()
        {
            Title = "Installation Database",
            Subject = pkg.Name,
            Author = pkg.Manufacturer,
            Keywords = "Installer",
            Comments = pkg.Description ??
                       $"This installer database contains the logic and data required to install {pkg.Name}.",
            Template = GetPlatformTemplate(pkg.Architecture),
            RevisionNumber = packageCode.ToString("B").ToUpperInvariant(),
            CodePage = 1252,
            CreatingApplication = "FalkForge",
            // WordCount 2 = compressed cabinet + long file-names support flag.
            WordCount = 2,
            // PageCount 200 = minimum required Windows Installer version (2.0).
            PageCount = 200,
            // Security 2 = read-only recommended (standard for shipped MSIs).
            Security = 2,
        };

        // Construct the recipe with an empty ContentHash placeholder, then
        // rebuild it via a with-expression carrying the digest. The hashing
        // payload deliberately excludes ContentHash itself, so the placeholder
        // never affects the output digest.
        // Collect all streams registered by producers (e.g. BinaryTableProducer)
        // into an immutable dictionary for the recipe. The registry uses ordinal
        // string comparison; ToImmutableDictionary preserves that comparer.
        ImmutableDictionary<string, StreamSource> streams =
            context.Streams.Snapshot().ToImmutableDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value,
                StringComparer.Ordinal);

        MsiDatabaseRecipe recipe = new()
        {
            Tables = finalTables,
            SummaryInfo = summaryInfo,
            Streams = streams,
            FileSequencing = ImmutableArray<FileSequenceEntry>.Empty,
            CabinetEmbeddings = ImmutableArray<CabinetEmbedding>.Empty,
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
            "RemoveIniFile" => MsiTableDefinitions.CreateRemoveIniFileTable,
            "CreateFolder" => MsiTableDefinitions.CreateCreateFolderTable,
            "DuplicateFile" => MsiTableDefinitions.CreateDuplicateFileTable,
            "Binary" => MsiTableDefinitions.CreateBinaryTable,
            "CustomAction" => MsiTableDefinitions.CreateCustomActionTable,
            "LockPermissions" => MsiTableDefinitions.CreateLockPermissionsTable,
            "MsiLockPermissionsEx" => MsiTableDefinitions.CreateMsiLockPermissionsExTable,
            "MIME" => MsiTableDefinitions.CreateMimeTable,
            "ProgId" => MsiTableDefinitions.CreateProgIdTable,
            "Extension" => MsiTableDefinitions.CreateExtensionTable,
            "Class" => MsiTableDefinitions.CreateClassTable,
            "TypeLib" => MsiTableDefinitions.CreateTypeLibTable,
            "MsiAssembly" => MsiTableDefinitions.CreateMsiAssemblyTable,
            "MsiAssemblyName" => MsiTableDefinitions.CreateMsiAssemblyNameTable,
            "Verb" => MsiTableDefinitions.CreateVerbTable,
            "MoveFile" => MsiTableDefinitions.CreateMoveFileTable,
            "RemoveFile" => MsiTableDefinitions.CreateRemoveFileTable,
            "InstallUISequence" => MsiTableDefinitions.CreateInstallUISequenceTable,
            "InstallExecuteSequence" => MsiTableDefinitions.CreateInstallExecuteSequenceTable,
            _ => throw new InvalidOperationException(
                $"No CREATE TABLE SQL registered for table '{table.Value}'."),
        };
    }

    private static string GetPlatformTemplate(ProcessorArchitecture architecture)
        => architecture switch
        {
            ProcessorArchitecture.X86 => "Intel;1033",
            ProcessorArchitecture.X64 => "x64;1033",
            ProcessorArchitecture.Arm64 => "Arm64;1033",
            _ => "x64;1033",
        };

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
