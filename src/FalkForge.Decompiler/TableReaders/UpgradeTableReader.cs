using FalkForge.Models;

namespace FalkForge.Decompiler.TableReaders;

/// <summary>
/// Reads the Upgrade table from an MSI database.
/// Columns: UpgradeCode, VersionMin, VersionMax, Language, Attributes, Remove, ActionProperty
/// </summary>
public static class UpgradeTableReader
{
    private static readonly string[] Columns = ["UpgradeCode", "VersionMin", "VersionMax", "Language", "Attributes", "Remove", "ActionProperty"];

    // MSI Upgrade table attribute flags
    private const int MsidbUpgradeAttributesMigrateFeatures = 0x00000001;
    private const int MsidbUpgradeAttributesOnlyDetect = 0x00000002;
    private const int MsidbUpgradeAttributesVersionMaxInclusive = 0x00000200;

    /// <summary>
    /// Result wrapper for upgrade table reading, since the table may not exist.
    /// </summary>
    public sealed class UpgradeReadResult
    {
        public MajorUpgradeModel? MajorUpgrade { get; init; }

        public static UpgradeReadResult Empty { get; } = new() { MajorUpgrade = null };
    }

    public static Result<UpgradeReadResult> Read(IMsiTableAccess tableAccess)
    {
        var existsResult = tableAccess.TableExists("Upgrade");
        if (existsResult.IsFailure)
            return Result<UpgradeReadResult>.Failure(existsResult.Error);
        if (!existsResult.Value)
            return UpgradeReadResult.Empty;

        var rowsResult = tableAccess.QueryTable("Upgrade", Columns);
        if (rowsResult.IsFailure)
            return Result<UpgradeReadResult>.Failure(ErrorKind.Validation, $"DEC003: Failed to read Upgrade table. {rowsResult.Error.Message}");

        if (rowsResult.Value.Count == 0)
            return UpgradeReadResult.Empty;

        // Analyze the upgrade rows to determine upgrade strategy
        var allowDowngrades = false;
        var allowSameVersion = false;
        var migrateFeatures = false;
        string? downgradeMessage = null;

        foreach (var row in rowsResult.Value)
        {
            _ = int.TryParse(row[4], out var attributes);

            var isDetectOnly = (attributes & MsidbUpgradeAttributesOnlyDetect) != 0;
            var hasMigrateFeatures = (attributes & MsidbUpgradeAttributesMigrateFeatures) != 0;
            var versionMaxInclusive = (attributes & MsidbUpgradeAttributesVersionMaxInclusive) != 0;

            if (hasMigrateFeatures)
                migrateFeatures = true;

            if (isDetectOnly && !string.IsNullOrEmpty(row[2]))
            {
                // Detect-only row with a max version often means "detect newer versions" for downgrade prevention
                allowDowngrades = false;
            }

            if (versionMaxInclusive)
                allowSameVersion = true;
        }

        return new UpgradeReadResult
        {
            MajorUpgrade = new MajorUpgradeModel
            {
                AllowDowngrades = allowDowngrades,
                AllowSameVersionUpgrades = allowSameVersion,
                DowngradeErrorMessage = downgradeMessage,
                MigrateFeatures = migrateFeatures
            }
        };
    }
}
