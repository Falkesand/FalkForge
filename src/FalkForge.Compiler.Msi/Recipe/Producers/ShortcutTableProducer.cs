using System.Collections.Immutable;
using System.Globalization;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>Shortcut</c> table. Walks
/// <see cref="PackageModel.Shortcuts"/> and emits one row per
/// (shortcut, location) pairing, mirroring the legacy <c>TableEmitter</c> (deleted in Phase 9)
/// <c>EmitShortcuts</c>. The 12-column shape covers the standard
/// Shortcut columns through <c>WkDir</c>; the four optional
/// DisplayResource* / DescriptionResource* columns are out of scope for
/// this batch — they're rarely populated in practice and the legacy <c>TableEmitter</c>
/// did not emit them.
/// </summary>
internal sealed class ShortcutTableProducer : ITableProducer
{
    private const string FallbackComponentId = "MainComponent";
    private const string DefaultWorkingDirectory = "INSTALLDIR";

    /// <summary>Static schema describing the <c>Shortcut</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        TableId directoryTable = WellKnownTableIds.Directory;
        TableId componentTable = WellKnownTableIds.Component;
        ResolvedPackage resolved = context.Resolved;
        string defaultComponentId =
            resolved.Components.Count > 0
                ? resolved.Components[0].Id
                : FallbackComponentId;

        ImmutableArray<RecipeRow>.Builder rows = ImmutableArray.CreateBuilder<RecipeRow>();
        int index = 0;
        foreach (ShortcutModel shortcut in resolved.Package.Shortcuts)
        {
            foreach (ShortcutLocation location in shortcut.Locations)
            {
                string directoryId = ResolveDirectoryId(location, shortcut);
                string shortcutId = string.Create(
                    CultureInfo.InvariantCulture,
                    $"SC_{index:D4}");
                index++;

                string target = string.Concat("[INSTALLDIR]", shortcut.TargetFile);

                ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                    new CellValue.StringValue(shortcutId),
                    new CellValue.ForeignKey(directoryTable, directoryId),
                    new CellValue.StringValue(shortcut.Name),
                    new CellValue.ForeignKey(componentTable, defaultComponentId),
                    new CellValue.StringValue(target),
                    shortcut.Arguments is null ? new CellValue.Null() : new CellValue.StringValue(shortcut.Arguments),
                    shortcut.Description is null ? new CellValue.Null() : new CellValue.StringValue(shortcut.Description),
                    new CellValue.Null(),
                    // Icon column: Icon table not yet a producer; declared as FK once IconTableProducer lands.
                    new CellValue.Null(),
                    new CellValue.Null(),
                    new CellValue.Null(),
                    new CellValue.StringValue(DefaultWorkingDirectory));
                rows.Add(new RecipeRow { Cells = cells });
            }
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static string ResolveDirectoryId(ShortcutLocation location, ShortcutModel shortcut)
    {
        return location switch
        {
            ShortcutLocation.Desktop => ProducerHelpers.DesktopFolderId,
            ShortcutLocation.StartMenu => shortcut.StartMenuSubfolder is not null
                ? ProducerHelpers.GetStartMenuSubfolderId(shortcut.StartMenuSubfolder)
                : ProducerHelpers.ProgramMenuFolderId,
            ShortcutLocation.Startup => ProducerHelpers.StartupFolderId,
            _ => ProducerHelpers.DesktopFolderId,
        };
    }

    private static TableSchema BuildSchema()
    {
        TableId directoryTable = WellKnownTableIds.Directory;
        TableId componentTable = WellKnownTableIds.Component;
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            RecipeColumn.String("Shortcut", 72),
            RecipeColumn.String("Directory_", 72),
            RecipeColumn.Localized("Name", 128),
            RecipeColumn.String("Component_", 72),
            RecipeColumn.String("Target", 255),
            RecipeColumn.String("Arguments", 255, nullable: true),
            RecipeColumn.Localized("Description", 255, nullable: true),
            RecipeColumn.Integer("Hotkey", 2, nullable: true),
            RecipeColumn.String("Icon_", 72, nullable: true),
            RecipeColumn.Integer("IconIndex", 2, nullable: true),
            RecipeColumn.Integer("ShowCmd", 2, nullable: true),
            RecipeColumn.String("WkDir", 72, nullable: true));

        return new TableSchema
        {
            Name = WellKnownTableIds.Shortcut,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray.Create(
                new ForeignKeySpec
                {
                    SourceColumn = new ColumnIndex(1),
                    TargetTable = directoryTable,
                },
                new ForeignKeySpec
                {
                    SourceColumn = new ColumnIndex(3),
                    TargetTable = componentTable,
                }),
        };
    }
}
