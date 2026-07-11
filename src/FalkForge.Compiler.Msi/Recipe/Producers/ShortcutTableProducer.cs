using System.Collections.Frozen;
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
        TableId iconTable = WellKnownTableIds.Icon;
        ResolvedPackage resolved = context.Resolved;
        string defaultComponentId =
            resolved.Components.Count > 0
                ? resolved.Components[0].Id
                : FallbackComponentId;

        // Directory ids already emitted by DirectoryTableProducer (which runs
        // first). WkDir must be a Directory table key, so an authored
        // WorkingDirectory is only honoured when it names one of these ids;
        // otherwise it falls back to the INSTALLDIR default. This never writes a
        // raw path into WkDir.
        FrozenSet<string> directoryIds = BuildDirectoryIdSet(context, directoryTable);

        ImmutableArray<RecipeRow>.Builder rows = ImmutableArray.CreateBuilder<RecipeRow>();
        int index = 0;
        foreach (ShortcutModel shortcut in resolved.Package.Shortcuts)
        {
            // Icon_ resolves to the shared Icon.Name emitted by IconTableProducer
            // for the same source path; IconIndex is meaningful only alongside an
            // icon, so both stay null when no icon is authored.
            (CellValue iconCell, CellValue iconIndexCell) = shortcut.IconFile is { Length: > 0 } iconFile
                ? ((CellValue)new CellValue.ForeignKey(iconTable, ProducerHelpers.ResolveIconName(iconFile)),
                    (CellValue)new CellValue.IntValue(shortcut.IconIndex))
                : ((CellValue)new CellValue.Null(), (CellValue)new CellValue.Null());

            string workingDirectoryId = ResolveWorkingDirectoryId(shortcut.WorkingDirectory, directoryIds);

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
                    iconCell,
                    iconIndexCell,
                    new CellValue.Null(),
                    new CellValue.StringValue(workingDirectoryId));
                rows.Add(new RecipeRow { Cells = cells });
            }
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    /// <summary>
    /// Resolves an authored <see cref="ShortcutModel.WorkingDirectory"/> to a
    /// Directory table key. Empty input keeps the INSTALLDIR default; a value
    /// that names an existing Directory id is used verbatim; anything else falls
    /// back to the default rather than writing a raw path into WkDir (which MSI
    /// requires to be a Directory key).
    /// </summary>
    private static string ResolveWorkingDirectoryId(string? workingDirectory, FrozenSet<string> directoryIds)
    {
        if (string.IsNullOrEmpty(workingDirectory))
        {
            return DefaultWorkingDirectory;
        }

        return directoryIds.Contains(workingDirectory) ? workingDirectory : DefaultWorkingDirectory;
    }

    private static FrozenSet<string> BuildDirectoryIdSet(RecipeBuildContext context, TableId directoryTable)
    {
        if (!context.BuiltTables.TryGetValue(directoryTable, out ImmutableArray<RecipeRow> directoryRows))
        {
            return FrozenSet<string>.Empty;
        }

        HashSet<string> ids = new(directoryRows.Length, StringComparer.Ordinal);
        foreach (RecipeRow row in directoryRows)
        {
            // Directory id is column 0, always a StringValue (see DirectoryTableProducer).
            if (row.Cells.Length > 0 && row.Cells[0] is CellValue.StringValue idCell)
            {
                ids.Add(idCell.Value);
            }
        }

        return ids.ToFrozenSet(StringComparer.Ordinal);
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
        TableId iconTable = WellKnownTableIds.Icon;
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
                },
                // Icon_ (column 8) references Icon.Name. Nullable: only validated
                // when the Icon table is emitted (i.e. some icon was authored).
                new ForeignKeySpec
                {
                    SourceColumn = new ColumnIndex(8),
                    TargetTable = iconTable,
                }),
        };
    }
}
