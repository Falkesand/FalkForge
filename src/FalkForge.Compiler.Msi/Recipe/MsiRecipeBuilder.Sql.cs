using System.Collections.Immutable;
using System.Text;
using FalkForge.Compiler.Msi.Tables;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// CREATE TABLE / INSERT view SQL lookups, and the platform summary-info
/// template string.
/// </summary>
public static partial class MsiRecipeBuilder
{
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
            "MsiServiceConfigFailureActions" => MsiTableDefinitions.CreateMsiServiceConfigFailureActionsTable,
            "Shortcut" => MsiTableDefinitions.CreateShortcutTable,
            "Environment" => MsiTableDefinitions.CreateEnvironmentTable,
            "Font" => MsiTableDefinitions.CreateFontTable,
            "LaunchCondition" => MsiTableDefinitions.CreateLaunchConditionTable,
            "IniFile" => MsiTableDefinitions.CreateIniFileTable,
            "RemoveIniFile" => MsiTableDefinitions.CreateRemoveIniFileTable,
            "CreateFolder" => MsiTableDefinitions.CreateCreateFolderTable,
            "DuplicateFile" => MsiTableDefinitions.CreateDuplicateFileTable,
            "Binary" => MsiTableDefinitions.CreateBinaryTable,
            "Icon" => MsiTableDefinitions.CreateIconTable,
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
