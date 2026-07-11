using System.Collections.Immutable;
using System.Globalization;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Producer for the MSI <c>Registry</c> table. Walks
/// <see cref="PackageModel.RegistryEntries"/> and projects each entry onto
/// the column shape used by the legacy <c>TableEmitter</c> (deleted in Phase 9)
/// <c>EmitRegistry</c>. Synthesises sequential <c>Reg_NNNN</c> identifiers
/// matching the legacy emitter and falls back to the first resolved
/// component (or <c>"MainComponent"</c>) when an entry omits an explicit
/// <see cref="RegistryEntryModel.ComponentId"/>.
/// </summary>
internal sealed class RegistryTableProducer : ITableProducer
{
    private const string FallbackComponentId = "MainComponent";

    /// <summary>Static schema describing the <c>Registry</c> table layout.</summary>
    public static readonly TableSchema TableSchema = BuildSchema();

    public TableSchema Schema => TableSchema;

    public Result<ImmutableArray<RecipeRow>> Produce(RecipeBuildContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        TableId componentTable = WellKnownTableIds.Component;
        ResolvedPackage resolved = context.Resolved;
        string defaultComponentId =
            resolved.Components.Count > 0
                ? resolved.Components[0].Id
                : FallbackComponentId;

        ImmutableArray<RecipeRow>.Builder rows = ImmutableArray.CreateBuilder<RecipeRow>();
        int index = 0;
        foreach (RegistryEntryModel entry in resolved.Package.RegistryEntries)
        {
            int root = MapRoot(entry.Root);
            string regId = string.Create(
                CultureInfo.InvariantCulture,
                $"Reg_{index:D4}");
            string componentId = ResolveComponentId(entry, index, resolved, defaultComponentId);
            index++;

            Result<string> valueResult = EncodeValue(entry);
            if (valueResult.IsFailure)
            {
                return Result<ImmutableArray<RecipeRow>>.Failure(valueResult.Error);
            }

            string valueText = valueResult.Value;

            CellValue nameCell = entry.ValueName is null
                ? new CellValue.Null()
                : new CellValue.StringValue(entry.ValueName);

            ImmutableArray<CellValue> cells = ImmutableArray.Create<CellValue>(
                new CellValue.StringValue(regId),
                new CellValue.IntValue(root),
                new CellValue.StringValue(entry.Key),
                nameCell,
                new CellValue.StringValue(valueText),
                new CellValue.ForeignKey(componentTable, componentId));
            rows.Add(new RecipeRow { Cells = cells });
        }

        return Result<ImmutableArray<RecipeRow>>.Success(rows.ToImmutable());
    }

    /// <summary>
    /// Resolves the Component_ FK for a registry row. An explicit
    /// <see cref="RegistryEntryModel.ComponentId"/> always wins (strongest, user-authored
    /// override). Otherwise, an entry with a FeatureRef (declared via FeatureBuilder.Registry)
    /// attaches to the dedicated component ComponentResolver synthesized for it — that component
    /// carries the FeatureRef, which is what places it under the correct feature in
    /// FeatureComponents. Without either, the entry falls back to the first resolved component
    /// (or "MainComponent"), matching the legacy TableEmitter default.
    /// </summary>
    private static string ResolveComponentId(
        RegistryEntryModel entry, int index, ResolvedPackage resolved, string defaultComponentId)
    {
        if (entry.ComponentId is not null)
        {
            return entry.ComponentId;
        }

        if (entry.FeatureRef is not null &&
            resolved.RegistryFeatureComponents.TryGetValue(index, out string? featureComponentId))
        {
            return featureComponentId;
        }

        return defaultComponentId;
    }

    /// <summary>
    /// Encodes <see cref="RegistryEntryModel.Value"/> per <see cref="RegistryEntryModel.ValueType"/>
    /// using the Windows Installer <c>Registry</c> table's type-prefix convention: no prefix (or a
    /// doubled leading <c>#</c> to escape a literal one) for REG_SZ, <c>#%</c> for REG_EXPAND_SZ,
    /// <c>#</c>+decimal for REG_DWORD, <c>#x</c>+hex for REG_BINARY, and <c>[~]</c>-delimited
    /// substrings for REG_MULTI_SZ. REG_QWORD has no native representation in this table, so a
    /// QWord entry fails the compile rather than being silently mis-encoded (see the comment on
    /// <c>RegistryKeyBuilder</c> for why no fluent QWord helper is offered).
    /// </summary>
    private static Result<string> EncodeValue(RegistryEntryModel entry)
    {
        switch (entry.ValueType)
        {
            case RegistryValueType.String:
                return Result<string>.Success(EscapeLiteralHash(ValueAsString(entry)));

            case RegistryValueType.ExpandString:
                return Result<string>.Success("#%" + ValueAsString(entry));

            case RegistryValueType.DWord:
                if (!TryToInt32(entry.Value, out int dword))
                {
                    return Result<string>.Failure(new Error(ErrorKind.Validation,
                        $"Registry entry '{entry.Key}\\{entry.ValueName}' declares RegistryValueType.DWord " +
                        "but its value is not a 32-bit integer."));
                }

                return Result<string>.Success("#" + dword.ToString(CultureInfo.InvariantCulture));

            case RegistryValueType.Binary:
                if (entry.Value is not byte[] bytes)
                {
                    return Result<string>.Failure(new Error(ErrorKind.Validation,
                        $"Registry entry '{entry.Key}\\{entry.ValueName}' declares RegistryValueType.Binary " +
                        "but its value is not a byte[]."));
                }

                return Result<string>.Success("#x" + Convert.ToHexString(bytes));

            case RegistryValueType.MultiString:
                if (entry.Value is not IReadOnlyList<string> values)
                {
                    return Result<string>.Failure(new Error(ErrorKind.Validation,
                        $"Registry entry '{entry.Key}\\{entry.ValueName}' declares RegistryValueType.MultiString " +
                        "but its value is not a string list."));
                }

                return Result<string>.Success(EncodeMultiString(values));

            case RegistryValueType.QWord:
                return Result<string>.Failure(new Error(ErrorKind.NotSupported,
                    $"Registry entry '{entry.Key}\\{entry.ValueName}' uses RegistryValueType.QWord. The MSI " +
                    "Registry table has no native 64-bit integer encoding (only REG_SZ, REG_EXPAND_SZ, " +
                    "REG_MULTI_SZ, REG_DWORD, and REG_BINARY are representable). Encode the value as " +
                    "RegistryValueType.Binary (8 little-endian bytes, e.g. via BitConverter.GetBytes) instead."));

            default:
                return Result<string>.Failure(new Error(ErrorKind.NotSupported,
                    $"Registry entry '{entry.Key}\\{entry.ValueName}' has an unrecognized RegistryValueType " +
                    $"'{entry.ValueType}'."));
        }
    }

    private static string ValueAsString(RegistryEntryModel entry)
    {
        return entry.Value as string ?? entry.Value?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// A REG_SZ value that legitimately starts with <c>#</c> must be escaped with a second
    /// leading <c>#</c> — otherwise the Windows Installer runtime reads the leading <c>#</c> as
    /// the start of one of the special type prefixes (<c>#x</c>, <c>#%</c>, <c>#&lt;int&gt;</c>)
    /// and mis-parses the value.
    /// </summary>
    private static string EscapeLiteralHash(string value)
    {
        return value.Length > 0 && value[0] == '#' ? "#" + value : value;
    }

    /// <summary>
    /// REG_MULTI_SZ entries are joined with the <c>[~]</c> delimiter. The Windows Installer
    /// runtime types a Registry value as REG_MULTI_SZ <b>solely</b> by the presence of a
    /// <c>[~]</c> sequence in the Value field (there is no separate type column), so the encoding
    /// must guarantee at least one <c>[~]</c> even when the list has a single element — otherwise
    /// a one-string multi-string silently installs as REG_SZ. Cases:
    /// <list type="bullet">
    ///   <item>Empty list — a bare <c>[~]</c> sentinel (empty REG_MULTI_SZ).</item>
    ///   <item>Single element — wrapped as <c>[~]value[~]</c> so the marker is always present;
    ///     on a fresh install (no pre-existing value) this yields the single-element list.</item>
    ///   <item>Two or more elements — joined as <c>a[~]b[~]c</c> (the documented REG_MULTI_SZ
    ///     "replace" form).</item>
    /// </list>
    /// Segment content is written verbatim (no hash-escaping): the <c>[~]</c> marker already
    /// forces the multi-string type, and doubling a leading <c>#</c> would instead downgrade the
    /// value back to REG_SZ. A first segment that itself begins with a bare <c>#</c> type-prefix
    /// character is a rare, unsupported edge case.
    /// </summary>
    private static string EncodeMultiString(IReadOnlyList<string> values)
    {
        return values.Count switch
        {
            0 => "[~]",
            1 => "[~]" + values[0] + "[~]",
            _ => string.Join("[~]", values),
        };
    }

    /// <summary>
    /// Coerces the boxed <see cref="RegistryEntryModel.Value"/> to a 32-bit integer for REG_DWORD
    /// encoding. Only integral CLR types are accepted; a floating-point or <c>decimal</c> value is
    /// rejected rather than rounded (e.g. <c>5.5</c> must fail loud, not silently become <c>6</c>),
    /// and a value outside <see cref="int"/> range fails via the caught <see cref="OverflowException"/>.
    /// Strings, <see cref="bool"/>, and other non-integral types are rejected outright.
    /// </summary>
    private static bool TryToInt32(object? value, out int result)
    {
        switch (value)
        {
            case int i:
                result = i;
                return true;
            case byte or sbyte or short or ushort or uint or long or ulong:
                try
                {
                    result = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                    return true;
                }
                catch (OverflowException)
                {
                    result = 0;
                    return false;
                }
            default:
                result = 0;
                return false;
        }
    }

    private static int MapRoot(RegistryRoot root)
    {
        return root switch
        {
            RegistryRoot.ClassesRoot => 0,
            RegistryRoot.CurrentUser => 1,
            RegistryRoot.LocalMachine => 2,
            RegistryRoot.Users => 3,
            _ => 2,
        };
    }

    private static TableSchema BuildSchema()
    {
        TableId componentTable = WellKnownTableIds.Component;
        ImmutableArray<RecipeColumn> columns = ImmutableArray.Create(
            RecipeColumn.String("Registry", 72),
            RecipeColumn.Integer("Root", 2),
            RecipeColumn.Localized("Key", 255),
            RecipeColumn.Localized("Name", 255, nullable: true),
            RecipeColumn.Localized("Value", 0, nullable: true),
            RecipeColumn.String("Component_", 72));

        return new TableSchema
        {
            Name = WellKnownTableIds.Registry,
            Columns = columns,
            PrimaryKey = ImmutableArray.Create(new ColumnIndex(0)),
            ForeignKeys = ImmutableArray.Create(new ForeignKeySpec
            {
                SourceColumn = new ColumnIndex(5),
                TargetTable = componentTable,
            }),
        };
    }
}
