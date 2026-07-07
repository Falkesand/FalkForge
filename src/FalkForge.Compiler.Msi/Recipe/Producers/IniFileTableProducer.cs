using System.Collections.Immutable;
using System.Globalization;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>IniFile</c> table. Walks
/// <see cref="PackageModel.IniFiles"/> and emits one row per entry,
/// mirroring <see cref="Tables.TableEmitter"/>'s <c>EmitIniFiles</c>:
/// each row is keyed on a synthesized <c>INI_NNNN</c> identifier matching
/// the legacy emitter, the <see cref="IniFileAction"/> enum projects to
/// its underlying integer, and every entry binds to the first resolved
/// component (or <c>"MainComponent"</c>) since the legacy emitter does
/// not honour a per-entry component reference.
/// </summary>
internal sealed class IniFileTableProducer : ITableProducer
{
    private const string FallbackComponentId = "MainComponent";
    private static readonly TableId ComponentTable = WellKnownTableIds.Component;

    /// <summary>Static schema describing the <c>IniFile</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ResolvedPackage resolved = context.Resolved;
        IReadOnlyList<IniFileModel> iniFiles = resolved.Package.IniFiles;

        if (iniFiles.Count == 0)
        {
            return Result<ImmutableArray<RecipeRow>>.Success(ImmutableArray<RecipeRow>.Empty);
        }

        string componentId =
            resolved.Components.Count > 0
                ? resolved.Components[0].Id
                : FallbackComponentId;

        ImmutableArray<RecipeRow>.Builder rows = ImmutableArray.CreateBuilder<RecipeRow>(iniFiles.Count);
        for (int index = 0; index < iniFiles.Count; index++)
        {
            IniFileModel ini = iniFiles[index];
            string iniId = string.Create(
                CultureInfo.InvariantCulture,
                $"INI_{index:D4}");

            CellValue dirCell = ini.DirProperty is null
                ? new CellValue.Null()
                : new CellValue.StringValue(ini.DirProperty);

            // Value column is nullable in the DDL even though IniFileModel
            // currently marks it required. Honour the null path so a future
            // relaxation of the model does not silently push a null through
            // CellValue.StringValue and produce a non-WiX-shaped MSI.
            CellValue valueCell = ini.Value is null
                ? new CellValue.Null()
                : new CellValue.StringValue(ini.Value);

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(iniId),
                new CellValue.StringValue(ini.FileName),
                dirCell,
                new CellValue.StringValue(ini.Section),
                new CellValue.StringValue(ini.Key),
                valueCell,
                new CellValue.IntValue((int)ini.Action),
                new CellValue.ForeignKey(ComponentTable, componentId));
            rows.Add(new RecipeRow { Cells = cells });
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static TableSchema BuildSchema()
    {
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            RecipeColumn.String("IniFile", 72),
            RecipeColumn.Localized("FileName", 255),
            RecipeColumn.String("DirProperty", 72, nullable: true),
            RecipeColumn.Localized("Section", 96),
            RecipeColumn.Localized("Key", 128),
            RecipeColumn.Localized("Value", 255, nullable: true),
            RecipeColumn.Integer("Action", 2),
            RecipeColumn.String("Component_", 72));

        return new TableSchema
        {
            Name = WellKnownTableIds.IniFile,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray.Create(new ForeignKeySpec
            {
                SourceColumn = new ColumnIndex(7),
                TargetTable = ComponentTable,
            }),
        };
    }
}
