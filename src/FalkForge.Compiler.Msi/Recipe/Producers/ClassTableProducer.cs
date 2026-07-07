using System.Collections.Immutable;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>Class</c> table — the Class-table slice of the
/// legacy <see cref="Tables.TableEmitter"/>'s <c>EmitComClasses</c>. One row
/// is emitted per <see cref="ComClassModel"/> entry. Columns map as:
/// <list type="bullet">
///   <item><c>CLSID</c> — <see cref="ComClassModel.ClassId"/> formatted with
///     "B" (braces) and uppercased, matching the legacy emitter.</item>
///   <item><c>Context</c> — <see cref="ComServerType.InprocServer32"/> →
///     <c>"InprocServer32"</c>; <see cref="ComServerType.LocalServer32"/> →
///     <c>"LocalServer32"</c>, matching the legacy emitter's ternary.</item>
///   <item><c>Component_</c> — <see cref="ComClassModel.ComponentRef"/> when
///     set; otherwise the first resolved component ID or <c>"MainComponent"</c>
///     fallback, matching the legacy emitter's <c>defaultComponentId</c>.</item>
///   <item><c>ProgId_Default</c> — <see cref="ComClassModel.ProgId"/>, null
///     when absent.</item>
///   <item><c>Description</c> — <see cref="ComClassModel.Description"/>, null
///     when absent.</item>
///   <item><c>AppId_</c> — <see cref="ComClassModel.AppId"/> formatted with
///     "B" (braces) and uppercased when present; null otherwise.</item>
///   <item><c>FileTypeMask</c> — always null; <see cref="ComClassModel"/> has
///     no FileTypeMask field and the legacy emitter writes null.</item>
///   <item><c>Icon_</c> — always null; <see cref="ComClassModel"/> has no
///     Icon field and the legacy emitter writes null.</item>
///   <item><c>IconIndex</c> — always 0, matching the legacy emitter.</item>
///   <item><c>DefInprocHandler</c> — for <see cref="ComServerType.InprocServer32"/>
///     servers: <see cref="ComClassModel.ThreadingModel"/> lowercased (e.g.
///     <c>"apartment"</c>, <c>"free"</c>, <c>"both"</c>, <c>"neutral"</c>);
///     null for <see cref="ComServerType.LocalServer32"/> servers, matching
///     the legacy emitter.</item>
///   <item><c>Argument</c> — always null; <see cref="ComClassModel"/> has no
///     Argument field and the legacy emitter writes null.</item>
///   <item><c>Feature_</c> — first feature ID or <c>"Complete"</c> fallback,
///     matching the legacy emitter's <c>defaultFeature</c>.</item>
/// </list>
///
/// Note: the legacy <c>EmitComClasses</c> also emits ProgId rows for COM
/// classes that have a <see cref="ComClassModel.ProgId"/>. That behaviour is
/// intentionally out of scope here — the dedicated
/// <see cref="ProgIdTableProducer"/> owns the <c>ProgId</c> table and sources
/// from <c>PackageModel.FileAssociations</c>. COM-class-originated ProgId rows
/// are a known divergence documented in the migration notes.
/// </summary>
internal sealed class ClassTableProducer : ITableProducer
{
    private const string FallbackComponentId = "MainComponent";
    private const string FallbackFeatureId = "Complete";

    /// <summary>Static schema describing the <c>Class</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ResolvedPackage resolved = context.Resolved;
        IReadOnlyList<ComClassModel> comClasses = resolved.Package.ComClasses;

        if (comClasses.Count == 0)
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
            ImmutableArray.CreateBuilder<RecipeRow>(comClasses.Count);

        foreach (ComClassModel cls in comClasses)
        {
            string clsid = cls.ClassId.ToString("B").ToUpperInvariant();

            string context2 = cls.ServerType == ComServerType.InprocServer32
                ? "InprocServer32"
                : "LocalServer32";

            string componentId = cls.ComponentRef ?? defaultComponentId;

            CellValue progIdCell = cls.ProgId is null
                ? new CellValue.Null()
                : new CellValue.StringValue(cls.ProgId);

            CellValue descriptionCell = cls.Description is null
                ? new CellValue.Null()
                : new CellValue.StringValue(cls.Description);

            CellValue appIdCell = cls.AppId is null
                ? new CellValue.Null()
                : new CellValue.StringValue(cls.AppId.Value.ToString("B").ToUpperInvariant());

            // DefInprocHandler holds the threading model string for inproc
            // servers; null for local servers — matches legacy emitter.
            CellValue defInprocHandlerCell = cls.ServerType == ComServerType.InprocServer32
                ? new CellValue.StringValue(cls.ThreadingModel.ToString().ToLowerInvariant())
                : new CellValue.Null();

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(clsid),          // CLSID
                new CellValue.StringValue(context2),        // Context
                new CellValue.StringValue(componentId),     // Component_
                progIdCell,                                  // ProgId_Default
                descriptionCell,                             // Description
                appIdCell,                                   // AppId_
                new CellValue.Null(),                        // FileTypeMask: no model field, always null
                new CellValue.Null(),                        // Icon_: no model field, always null
                new CellValue.IntValue(0),                   // IconIndex: always 0 per legacy emitter
                defInprocHandlerCell,                        // DefInprocHandler
                new CellValue.Null(),                        // Argument: no model field, always null
                new CellValue.StringValue(defaultFeatureId)); // Feature_

            rows.Add(new RecipeRow { Cells = cells });
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    private static TableSchema BuildSchema()
    {
        TableId componentTable = WellKnownTableIds.Component;
        TableId appIdTable = WellKnownTableIds.AppId;
        TableId iconTable = WellKnownTableIds.Icon;
        TableId featureTable = WellKnownTableIds.Feature;

        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            RecipeColumn.String("CLSID", 38),
            RecipeColumn.String("Context", 32),
            RecipeColumn.String("Component_", 72),
            RecipeColumn.String("ProgId_Default", 255, nullable: true),
            RecipeColumn.String("Description", 255, nullable: true),
            // AppId_ nullable per DDL; producer writes null when ComClassModel.AppId is absent
            RecipeColumn.String("AppId_", 38, nullable: true),
            // FileTypeMask nullable per DDL; ComClassModel has no such field — always null
            RecipeColumn.String("FileTypeMask", 255, nullable: true),
            // Icon_ nullable per DDL; ComClassModel has no Icon field — always null
            RecipeColumn.String("Icon_", 72, nullable: true),
            // SHORT in MSI DDL; nullable per DDL; legacy emitter always writes 0
            RecipeColumn.Integer("IconIndex", 2, nullable: true),
            // DefInprocHandler nullable per DDL; holds threading model string
            // for InprocServer32 classes; null for LocalServer32 classes
            RecipeColumn.String("DefInprocHandler", 32, nullable: true),
            // Argument nullable per DDL; ComClassModel has no Argument field — always null
            RecipeColumn.String("Argument", 255, nullable: true),
            RecipeColumn.String("Feature_", 38));

        return new TableSchema
        {
            Name = WellKnownTableIds.Class,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(
                new ColumnIndex(0),
                new ColumnIndex(1),
                new ColumnIndex(2)),
            ForeignKeys = ImmutableArray.Create(
                new ForeignKeySpec
                {
                    SourceColumn = new ColumnIndex(2),
                    TargetTable = componentTable,
                },
                new ForeignKeySpec
                {
                    SourceColumn = new ColumnIndex(5),
                    TargetTable = appIdTable,
                },
                new ForeignKeySpec
                {
                    SourceColumn = new ColumnIndex(7),
                    TargetTable = iconTable,
                },
                new ForeignKeySpec
                {
                    SourceColumn = new ColumnIndex(11),
                    TargetTable = featureTable,
                }),
        };
    }
}
