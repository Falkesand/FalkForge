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
        TableId componentTable = TableId.Create("Component").Value;
        TableId appIdTable = TableId.Create("AppId").Value;
        TableId iconTable = TableId.Create("Icon").Value;
        TableId featureTable = TableId.Create("Feature").Value;

        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            new RecipeColumn
            {
                Name = "CLSID",
                Type = ColumnType.String,
                Width = 38,
                Nullable = false,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Context",
                Type = ColumnType.String,
                Width = 32,
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
                Name = "ProgId_Default",
                Type = ColumnType.String,
                Width = 255,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Description",
                Type = ColumnType.String,
                Width = 255,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                // AppId_ nullable per DDL; producer writes null when ComClassModel.AppId is absent
                Name = "AppId_",
                Type = ColumnType.String,
                Width = 38,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                // FileTypeMask nullable per DDL; ComClassModel has no such field — always null
                Name = "FileTypeMask",
                Type = ColumnType.String,
                Width = 255,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                // Icon_ nullable per DDL; ComClassModel has no Icon field — always null
                Name = "Icon_",
                Type = ColumnType.String,
                Width = 72,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                // SHORT in MSI DDL; nullable per DDL; legacy emitter always writes 0
                Name = "IconIndex",
                Type = ColumnType.Integer,
                Width = 2,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                // DefInprocHandler nullable per DDL; holds threading model string
                // for InprocServer32 classes; null for LocalServer32 classes
                Name = "DefInprocHandler",
                Type = ColumnType.String,
                Width = 32,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                // Argument nullable per DDL; ComClassModel has no Argument field — always null
                Name = "Argument",
                Type = ColumnType.String,
                Width = 255,
                Nullable = true,
                LocalizableKey = false,
            },
            new RecipeColumn
            {
                Name = "Feature_",
                Type = ColumnType.String,
                Width = 38,
                Nullable = false,
                LocalizableKey = false,
            });

        return new TableSchema
        {
            Name = TableId.Create("Class").Value,
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
