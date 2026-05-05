using System.Collections.Immutable;
using FalkForge.Compiler.Msi.Tables;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>Directory</c> table. Synthesizes the full directory
/// tree from <see cref="ResolvedPackage"/>: every component / file install path
/// contributes its known-folder root and per-segment intermediate IDs, the
/// configured install directory leaf is materialized as
/// <see cref="WellKnownDirectoryIds.InstallDir"/>, and the implicit
/// <see cref="WellKnownDirectoryIds.TargetDir"/> sits at the root.
/// </summary>
/// <remarks>
/// Mirrors <c>TableEmitter.EmitDirectories</c>. The recipe pipeline cannot lean
/// on <see cref="PackageModel.Directories"/> because builders rarely populate
/// that list — directory rows are derived data, not authored data. Producing
/// them here keeps the recipe path independent from <c>TableEmitter</c> while
/// still emitting the same identifiers component <c>Directory_</c> foreign keys
/// expect.
/// </remarks>
internal sealed class DirectoryTableProducer : ITableProducer
{
    /// <summary>Static schema describing the <c>Directory</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        TableId directoryTable = TableId.Create("Directory").Value;
        ImmutableArray<RecipeRow>.Builder rows = ImmutableArray.CreateBuilder<RecipeRow>();
        HashSet<string> emitted = new(StringComparer.Ordinal);

        // Step 1: TARGETDIR is the implicit MSI root; emit unconditionally with
        // a null parent. The DefaultDir literal "SourceDir" is the WiX/MSI
        // convention and matches the legacy emitter exactly.
        AddRow(rows, emitted, directoryTable, WellKnownDirectoryIds.TargetDir, parent: null, name: "SourceDir");

        InstallPath? installDir = context.Resolved.Package.DefaultInstallDirectory;

        // Step 2: walk the configured install directory first so the leaf gets
        // the canonical "INSTALLDIR" id. Component walks reuse those rows via
        // the emitted-set rather than re-emitting under generated D_* ids.
        if (installDir is not null)
        {
            EnsureInstallPathRows(rows, emitted, directoryTable, installDir, installDir);
        }

        // Step 3: every component's directory; covers paths outside the install
        // dir (e.g. CommonAppData, alt KnownFolders) and any subdirectories
        // beneath the install dir leaf.
        foreach (ResolvedComponent component in context.Resolved.Components)
        {
            EnsureInstallPathRows(rows, emitted, directoryTable, component.Directory, installDir);
        }

        // Step 4: files may target directories that no resolved component
        // points at directly (file producers reach into ResolvedPackage.Files
        // for sequencing). Walking files closes that loophole so the FK
        // validator never sees an unresolved Directory_ reference.
        foreach (ResolvedFile file in context.Resolved.Files)
        {
            EnsureInstallPathRows(rows, emitted, directoryTable, file.TargetDirectory, installDir);
        }

        // Step 5: shortcut system directories. Shortcuts may reference special
        // MSI virtual directories (DesktopFolder, ProgramMenuFolder, StartupFolder,
        // SM_<subfolder>_<hash>) that have no corresponding component or file path.
        // The legacy TableEmitter emits these in EmitDirectories unconditionally when
        // shortcuts are present. Mirror that behaviour here so the FK validator never
        // sees unresolved Directory_ references from ShortcutTableProducer.
        PackageModel pkg = context.Resolved.Package;
        if (pkg.Shortcuts.Count > 0)
        {
            bool needsDesktop = false;
            bool needsProgramMenu = false;
            bool needsStartup = false;

            foreach (ShortcutModel shortcut in pkg.Shortcuts)
            {
                foreach (ShortcutLocation location in shortcut.Locations)
                {
                    switch (location)
                    {
                        case ShortcutLocation.Desktop:
                            needsDesktop = true;
                            break;
                        case ShortcutLocation.StartMenu:
                            needsProgramMenu = true;
                            if (shortcut.StartMenuSubfolder is not null)
                            {
                                string smId = ProducerHelpers.GetStartMenuSubfolderId(shortcut.StartMenuSubfolder);
                                // Subfolder parent is ProgramMenuFolder; use the subfolder
                                // display name as DefaultDir so the installer creates the
                                // correct folder name (matches legacy EmitDirectories).
                                if (emitted.Add(smId))
                                {
                                    rows.Add(MakeRow(
                                        directoryTable,
                                        id: smId,
                                        parentId: ProducerHelpers.ProgramMenuFolderId,
                                        name: shortcut.StartMenuSubfolder));
                                }
                            }
                            break;
                        case ShortcutLocation.Startup:
                            needsStartup = true;
                            break;
                    }
                }
            }

            if (needsDesktop)
                AddRow(rows, emitted, directoryTable, ProducerHelpers.DesktopFolderId,
                    parent: WellKnownDirectoryIds.TargetDir, name: ".");
            if (needsProgramMenu)
                AddRow(rows, emitted, directoryTable, ProducerHelpers.ProgramMenuFolderId,
                    parent: WellKnownDirectoryIds.TargetDir, name: ".");
            if (needsStartup)
                AddRow(rows, emitted, directoryTable, ProducerHelpers.StartupFolderId,
                    parent: WellKnownDirectoryIds.TargetDir, name: ".");
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static void EnsureInstallPathRows(
        ImmutableArray<RecipeRow>.Builder rows,
        HashSet<string> emitted,
        TableId directoryTable,
        InstallPath path,
        InstallPath? installDir)
    {
        // The known-folder root (e.g. ProgramFilesFolder) is always parented to
        // TARGETDIR with the dot DefaultDir convention. Standard MSI directory
        // tokens take their real path from the platform at install time, so
        // the row body is purely a placeholder.
        if (emitted.Add(path.Root.Token))
        {
            rows.Add(MakeRow(
                directoryTable,
                id: path.Root.Token,
                parentId: WellKnownDirectoryIds.TargetDir,
                name: "."));
        }

        // Walk each path segment, using the synthesizer to compute the id at
        // depth i. Parents are guaranteed to have been emitted on prior loop
        // iterations or by the install-dir walk → FK-safe topological order.
        IReadOnlyList<string> segments = path.Segments;
        string currentParent = path.Root.Token;
        for (int i = 0; i < segments.Count; i++)
        {
            InstallPath prefix = DirectoryTreeSynthesizer.BuildPrefixPath(path, i + 1);
            string segDirId = DirectoryTreeSynthesizer.ComputeDirectoryId(prefix, installDir);

            if (emitted.Add(segDirId))
            {
                rows.Add(MakeRow(
                    directoryTable,
                    id: segDirId,
                    parentId: currentParent,
                    name: segments[i]));
            }

            currentParent = segDirId;
        }
    }

    private static void AddRow(
        ImmutableArray<RecipeRow>.Builder rows,
        HashSet<string> emitted,
        TableId directoryTable,
        string id,
        string? parent,
        string name)
    {
        if (!emitted.Add(id))
        {
            return;
        }

        rows.Add(MakeRow(directoryTable, id, parent, name));
    }

    private static RecipeRow MakeRow(TableId directoryTable, string id, string? parentId, string name)
    {
        CellValue parentCell = parentId is null
            ? new CellValue.Null()
            : new CellValue.ForeignKey(directoryTable, parentId);

        ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
            new CellValue.StringValue(id),
            parentCell,
            new CellValue.StringValue(name));
        return new RecipeRow { Cells = cells };
    }

    private static TableSchema BuildSchema()
    {
        TableId directoryTable = TableId.Create("Directory").Value;
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            new RecipeColumn
            {
                Name = "Directory",
                Type = ColumnType.String,
                Width = 72,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Directory_Parent",
                Type = ColumnType.String,
                Width = 72,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "DefaultDir",
                Type = ColumnType.Localized,
                Width = 255,
                Nullable = false,
                LocalizableKey = false,
            });

        return new TableSchema
        {
            Name = directoryTable,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray.Create(new ForeignKeySpec
            {
                SourceColumn = new ColumnIndex(1),
                TargetTable = directoryTable,
            }),
        };
    }
}
