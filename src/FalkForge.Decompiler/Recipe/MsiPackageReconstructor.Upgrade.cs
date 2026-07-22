using FalkForge.Decompiler.Recipe.Schemas;
using FalkForge.Models;

namespace FalkForge.Decompiler.Recipe;

/// <summary>
/// Upgrade-table reconstruction: derives the <see cref="MajorUpgradeModel"/> /
/// <see cref="DowngradeModel"/> pair from the <c>msidbUpgradeAttributes*</c> bit
/// flags on each <see cref="UpgradeRow"/>.
/// </summary>
public static partial class MsiPackageReconstructor
{
    private const int MsidbUpgradeAttributesMigrateFeatures = 0x00000001;
    private const int MsidbUpgradeAttributesOnlyDetect       = 0x00000002;
    private const int MsidbUpgradeAttributesVersionMaxIncl   = 0x00000200;

    private static (MajorUpgradeModel? Major, DowngradeModel? Downgrade) ReconstructUpgrade(
        IReadOnlyList<UpgradeRow> rows)
    {
        if (rows.Count == 0)
            return (null, null);

        var allowSameVersion = false;
        var migrateFeatures  = false;
        var downgradeBlocked = false;

        foreach (var row in rows)
        {
            var attrs = row.Attributes;
            if ((attrs & MsidbUpgradeAttributesMigrateFeatures) != 0) migrateFeatures  = true;
            // UpgradeTableProducer writes the NEWERVERSIONFOUND/OnlyDetect row with the current
            // version in VersionMin and an EMPTY VersionMax — so the "is this a real detection
            // row" check must key off VersionMin, not VersionMax.
            if ((attrs & MsidbUpgradeAttributesOnlyDetect)       != 0 &&
                !string.IsNullOrEmpty(row.VersionMin))              downgradeBlocked = true;
            if ((attrs & MsidbUpgradeAttributesVersionMaxIncl)   != 0) allowSameVersion = true;
        }

        var downgrade = downgradeBlocked
            ? new DowngradeModel { AllowDowngrades = false, ErrorMessage = null }
            : new DowngradeModel { AllowDowngrades = true,  ErrorMessage = null };

        return (
            new MajorUpgradeModel
            {
                AllowSameVersionUpgrades = allowSameVersion,
                MigrateFeatures = migrateFeatures
            },
            downgrade);
    }
}
