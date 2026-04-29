using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>Shortcut</c> table. Walks
/// <see cref="PackageModel.Shortcuts"/> and emits one row per
/// (shortcut, location) pairing, mirroring <see cref="TableEmitter"/>'s
/// <c>EmitShortcuts</c>. The 12-column shape covers the standard
/// Shortcut columns through <c>WkDir</c>; the four optional
/// DisplayResource* / DescriptionResource* columns are out of scope for
/// this batch — they're rarely populated in practice and TableEmitter
/// itself does not emit them.
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

        TableId directoryTable = TableId.Create("Directory").Value;
        TableId componentTable = TableId.Create("Component").Value;
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
            ShortcutLocation.Desktop => "DesktopFolder",
            ShortcutLocation.StartMenu => shortcut.StartMenuSubfolder is not null
                ? GetStartMenuSubfolderId(shortcut.StartMenuSubfolder)
                : "ProgramMenuFolder",
            ShortcutLocation.Startup => "StartupFolder",
            _ => "DesktopFolder",
        };
    }

    private static string GetStartMenuSubfolderId(string subfolder)
    {
        string id = string.Create(
            CultureInfo.InvariantCulture,
            $"SM_{SanitizeId(subfolder)}_{StableHash(subfolder)}");
        return id.Length > 72 ? id[..72] : id;
    }

    private static string SanitizeId(string name)
    {
        char[] sanitized = new char[name.Length];
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            sanitized[i] = char.IsLetterOrDigit(c) || c == '_' || c == '.' ? c : '_';
        }

        return new string(sanitized);
    }

    private static string StableHash(string input)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes, 0, 4);
    }

    private static TableSchema BuildSchema()
    {
        TableId directoryTable = TableId.Create("Directory").Value;
        TableId componentTable = TableId.Create("Component").Value;
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            new RecipeColumn
            {
                Name = "Shortcut",
                Type = ColumnType.String,
                Width = 72,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Directory_",
                Type = ColumnType.String,
                Width = 72,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Name",
                Type = ColumnType.Localized,
                Width = 128,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Component_",
                Type = ColumnType.String,
                Width = 72,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Target",
                Type = ColumnType.String,
                Width = 255,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Arguments",
                Type = ColumnType.String,
                Width = 255,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Description",
                Type = ColumnType.Localized,
                Width = 255,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Hotkey",
                Type = ColumnType.Integer,
                Width = 2,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Icon_",
                Type = ColumnType.String,
                Width = 72,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "IconIndex",
                Type = ColumnType.Integer,
                Width = 2,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "ShowCmd",
                Type = ColumnType.Integer,
                Width = 2,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "WkDir",
                Type = ColumnType.String,
                Width = 72,
                Nullable = true,
                LocalizableKey = false,
            });

        return new TableSchema
        {
            Name = TableId.Create("Shortcut").Value,
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
