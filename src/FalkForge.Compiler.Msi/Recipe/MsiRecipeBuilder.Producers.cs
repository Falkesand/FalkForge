using System.Collections.Immutable;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Diagnostics;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Runs the fixed built-in table-producer pipeline.
/// </summary>
public static partial class MsiRecipeBuilder
{
    private static Result<ImmutableArray<RecipeTable>> RunBuiltInProducers(
        RecipeBuildContext context,
        bool logProducerDebug,
        IFalkLogger? logger)
    {
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
        // MsiServiceConfigFailureActionsTableProducer has no legacy counterpart (issue C3
        // fix) — it is slotted next to the other service producers and is likewise
        // EmitWhenEmpty=false so packages without ServiceBuilder.FailureActions(...) are
        // unaffected. IconTableProducer is the other new-only producer (not part of the legacy
        // enumeration above) — it is slotted after Binary with EmitWhenEmpty=false, so packages
        // with no icon (ProductIcon/shortcut icons) are unaffected and the ordering invariant
        // above still holds for every table legacy actually emitted.
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
            new MsiServiceConfigFailureActionsTableProducer(),
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
            new IconTableProducer(),
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
                logger?.Log(LogLevel.Error, "MsiRecipeBuilder",
                    $"Producer '{producer.Schema.Name}' failed: {producerResult.Error.Message}",
                    new Dictionary<string, string> { ["code"] = producerResult.Error.Kind.ToString() });
                return Result<ImmutableArray<RecipeTable>>.Failure(producerResult.Error);
            }

            ImmutableArray<RecipeRow> rows = producerResult.Value;
            context.AddBuiltTable(producer.Schema.Name, rows);

            if (logProducerDebug)
                logger!.Debug("MsiRecipeBuilder", $"Producer '{producer.Schema.Name}' produced {rows.Length} row(s).");

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

        return Result<ImmutableArray<RecipeTable>>.Success(tableBuilder.ToImmutable());
    }
}
