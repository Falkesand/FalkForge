using System.Collections.Immutable;
using FalkForge.Compiler.Msi.Tables;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>Component</c> table. Walks
/// <see cref="ResolvedPackage.Components"/> and projects each
/// <see cref="ResolvedComponent"/> onto a <c>Component</c> table row matching
/// the column shape used by the legacy <c>TableEmitter</c> (deleted in Phase 9)
/// <c>EmitComponents</c>. The Attributes value mirrors the legacy emitter:
/// the 64-bit bit (256) for x64/Arm64 packages, the NeverOverwrite bit
/// (0x80) and Permanent bit (0x10) when the component is so flagged.
/// </summary>
internal sealed class ComponentTableProducer : ITableProducer
{
    private const int Component64BitAttribute = 256;
    private const int ComponentNeverOverwriteAttribute = 0x80;
    private const int ComponentPermanentAttribute = 0x10;

    /// <summary>Static schema describing the <c>Component</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ResolvedPackage resolved = context.Resolved;
        TableId directoryTable = WellKnownTableIds.Directory;
        bool sixtyFourBit =
            resolved.Package.Architecture is ProcessorArchitecture.X64 or ProcessorArchitecture.Arm64;

        ImmutableArray<RecipeRow>.Builder rows = ImmutableArray.CreateBuilder<RecipeRow>();

        // Synthesize a "MainComponent" placeholder when no real components are resolved
        // (e.g., no-file packages). Other producers fall back to "MainComponent" as the
        // component FK target; without this row the FK validator rejects those references.
        // The placeholder uses a zero GUID and TARGETDIR so the MSI is structurally valid.
        // This matches the implicit expectation of the legacy TableEmitter (deleted in Phase 9)
        // which inserts "MainComponent" raw strings without validation and relies on msi.dll's leniency.
        if (resolved.Components.Count == 0)
        {
            ImmutableArray<CellValue> placeholderCells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue("MainComponent"),
                new CellValue.StringValue("{00000000-0000-0000-0000-000000000000}"),
                new CellValue.ForeignKey(directoryTable, WellKnownDirectoryIds.TargetDir),
                new CellValue.IntValue(0),
                new CellValue.StringValue(string.Empty),
                new CellValue.StringValue(string.Empty));
            rows.Add(new RecipeRow { Cells = placeholderCells });
        }

        foreach (ResolvedComponent component in resolved.Components)
        {
            int attributes = sixtyFourBit ? Component64BitAttribute : 0;
            if (component.NeverOverwrite)
            {
                attributes |= ComponentNeverOverwriteAttribute;
            }

            if (component.Permanent)
            {
                attributes |= ComponentPermanentAttribute;
            }

            string guidString = component.Guid.ToString("B").ToUpperInvariant();
            // Resolve the component's directory FK against the synthesized
            // tree. Using the bare KnownFolder root (e.g. ProgramFilesFolder)
            // would be wrong whenever the component sits below the install dir
            // because DirectoryTableProducer emits intermediate D_* and the
            // canonical INSTALLDIR rows; the FK must point at the leaf row,
            // not at the root.
            string directoryId = context.GetOrComputeDirectoryId(component.Directory);
            // Component.Condition is nullable in MSI but the legacy TableEmitter (deleted in Phase 9)
            // writes empty string when no condition is set; mirror that to keep recipe row
            // shape identical.
            string condition = component.Condition ?? string.Empty;

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(component.Id),
                new CellValue.StringValue(guidString),
                new CellValue.ForeignKey(directoryTable, directoryId),
                new CellValue.IntValue(attributes),
                new CellValue.StringValue(condition),
                new CellValue.StringValue(component.KeyPath));
            rows.Add(new RecipeRow { Cells = cells });
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static TableSchema BuildSchema()
    {
        TableId directoryTable = WellKnownTableIds.Directory;
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            RecipeColumn.String("Component", 72),
            RecipeColumn.String("ComponentId", 38, nullable: true),
            RecipeColumn.String("Directory_", 72),
            RecipeColumn.Integer("Attributes", 2),
            RecipeColumn.String("Condition", 255, nullable: true),
            RecipeColumn.String("KeyPath", 72, nullable: true));

        return new TableSchema
        {
            Name = WellKnownTableIds.Component,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray.Create(new ForeignKeySpec
            {
                SourceColumn = new ColumnIndex(2),
                TargetTable = directoryTable,
            }),
        };
    }
}
