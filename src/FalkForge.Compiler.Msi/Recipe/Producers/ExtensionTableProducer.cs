using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>Extension</c> table — the Extension-table
/// slice of the legacy <see cref="Tables.TableEmitter"/>'s
/// <c>EmitFileAssociations</c> partition. Cells project as
/// (<c>Extension</c> with the leading dot stripped to match the
/// table's bare-suffix primary key, <c>Component_</c> with the standard
/// first-resolved-component / <c>"MainComponent"</c> fallback chain,
/// <c>ProgId_</c>, <c>MIME_</c> from the optional
/// <see cref="FileAssociationModel.ContentType"/>, and <c>Feature_</c>
/// with a fallback to the first defined feature or the synthetic
/// <c>"Complete"</c> feature emitted by <see cref="MsiAuthoring"/>).
/// The Extension branch fires unconditionally per association — no
/// ContentType / Verbs filter — and shares the
/// <see cref="ProgIdTableProducer"/>'s emit-per-association predicate.
///
/// Note: dedicated producers for <c>ProgId</c>, <c>MIME</c>, and
/// <c>Verb</c> handle the other three tables in the
/// <c>EmitFileAssociations</c> partition and are intentionally out of
/// scope here so the producer set partitions the input list cleanly.
/// </summary>
internal sealed class ExtensionTableProducer : ITableProducer
{
    private const string FallbackComponentId = "MainComponent";
    private const string FallbackFeatureId = "Complete";

    /// <summary>Static schema describing the <c>Extension</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ResolvedPackage resolved = context.Resolved;
        IReadOnlyList<FileAssociationModel> associations =
            resolved.Package.FileAssociations;

        if (associations.Count == 0)
        {
            return Result<ImmutableArray<RecipeRow>>.Success(ImmutableArray<RecipeRow>.Empty);
        }

        string defaultComponentId =
            resolved.Components.Count > 0
                ? resolved.Components[0].Id
                : FallbackComponentId;
        string defaultFeatureId =
            resolved.Package.Features.Count > 0
                ? resolved.Package.Features[0].Id
                : FallbackFeatureId;

        ImmutableArray<RecipeRow>.Builder rows =
            ImmutableArray.CreateBuilder<RecipeRow>(associations.Count);
        for (int index = 0; index < associations.Count; index++)
        {
            FileAssociationModel assoc = associations[index];
            string ext = assoc.Extension.TrimStart('.');

            CellValue mimeCell = assoc.ContentType is null
                ? new CellValue.Null()
                : new CellValue.StringValue(assoc.ContentType);

            string componentId = ResolveComponentId(assoc, index, resolved, defaultComponentId);
            string featureId = assoc.FeatureRef ?? defaultFeatureId;

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(ext),
                new CellValue.StringValue(componentId),
                new CellValue.StringValue(assoc.ProgId),
                mimeCell,
                new CellValue.StringValue(featureId));
            rows.Add(new RecipeRow { Cells = cells });
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    /// <summary>
    /// Resolves the Component_ FK for an Extension row. An entry with a FeatureRef (declared via
    /// FeatureBuilder.FileAssociation(...)) attaches to the dedicated component ComponentResolver
    /// synthesized for it — that component carries the FeatureRef, which is what places it under
    /// the correct feature in FeatureComponents (in addition to the Extension table's own
    /// Feature_ column, set directly from FeatureRef above). Without one, the entry falls back to
    /// the first resolved component (or "MainComponent"), matching the legacy default.
    /// </summary>
    private static string ResolveComponentId(
        FileAssociationModel assoc, int index, ResolvedPackage resolved, string defaultComponentId)
    {
        if (assoc.FeatureRef is not null &&
            resolved.FileAssociationFeatureComponents.TryGetValue(index, out string? featureComponentId))
        {
            return featureComponentId;
        }

        return defaultComponentId;
    }

    private static TableSchema BuildSchema()
    {
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            RecipeColumn.String("Extension", 255),
            RecipeColumn.String("Component_", 72),
            RecipeColumn.String("ProgId_", 255, nullable: true),
            RecipeColumn.String("MIME_", 64, nullable: true),
            RecipeColumn.String("Feature_", 38));

        return new TableSchema
        {
            Name = WellKnownTableIds.Extension,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0), new ColumnIndex(1)),
            ForeignKeys = ImmutableArray<ForeignKeySpec>.Empty,
        };
    }
}
