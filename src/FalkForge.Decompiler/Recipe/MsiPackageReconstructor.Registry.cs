using FalkForge.Decompiler.Recipe.Schemas;
using FalkForge.Models;

namespace FalkForge.Decompiler.Recipe;

/// <summary>
/// Registry-table reconstruction: maps raw <see cref="RegistryRow"/> entries into
/// <see cref="RegistryEntryModel"/>, decoding the Windows Installer
/// <c>Registry.Value</c> type-prefix convention back into typed values.
/// </summary>
public static partial class MsiPackageReconstructor
{
    private static List<RegistryEntryModel> BuildRegistryEntries(IReadOnlyList<RegistryRow> registryRows)
    {
        return registryRows
            .Select(r =>
            {
                var (regValue, regType) = ParseRegistryValue(r.Value);
                return new RegistryEntryModel
                {
                    Root = MapRegistryRoot(r.Root),
                    Key = r.Key,
                    ValueName = r.Name,
                    Value = regValue,
                    ValueType = regType,
                    ComponentId = r.Component_
                };
            })
            .ToList();
    }

    private static RegistryRoot MapRegistryRoot(int msiRoot) => msiRoot switch
    {
        0 => RegistryRoot.ClassesRoot,
        1 => RegistryRoot.CurrentUser,
        2 => RegistryRoot.LocalMachine,
        3 => RegistryRoot.Users,
        _ => RegistryRoot.LocalMachine
    };

    /// <summary>
    /// Exact inverse of <c>RegistryTableProducer.EncodeValue</c>. Decodes the Windows Installer
    /// <c>Registry.Value</c> type-prefix convention back into a typed
    /// (<see cref="object"/>, <see cref="RegistryValueType"/>) pair:
    /// <list type="bullet">
    ///   <item><c>[~]</c>-delimited → REG_MULTI_SZ (<see cref="List{String}"/>), covering the
    ///     producer's <c>[~]</c> (empty), <c>[~]value[~]</c> (single) and <c>a[~]b[~]c</c> (multi) forms.</item>
    ///   <item><c>#x</c>+hex → REG_BINARY (<c>byte[]</c>).</item>
    ///   <item><c>#%</c>+text → REG_EXPAND_SZ.</item>
    ///   <item><c>##</c>+text → REG_SZ whose literal value begins with '#' (producer doubles a leading '#').</item>
    ///   <item><c>#</c>+decimal → REG_DWORD (<see cref="int"/>).</item>
    ///   <item>no prefix → REG_SZ.</item>
    /// </list>
    /// The <c>[~]</c> test comes first because REG_MULTI_SZ is typed solely by that delimiter's
    /// presence (there is no separate type column); the <c>#x</c>/<c>#%</c>/<c>##</c> tests precede
    /// the bare <c>#</c> DWORD test so those two-character prefixes are never mis-read as a decimal.
    /// </summary>
    private static (object? Value, RegistryValueType Type) ParseRegistryValue(string? rawValue)
    {
        if (rawValue is null)
            return (null, RegistryValueType.String);

        if (rawValue.Contains("[~]", StringComparison.Ordinal))
            return (DecodeMultiString(rawValue), RegistryValueType.MultiString);

        if (rawValue.StartsWith("#x", StringComparison.Ordinal))
        {
            if (TryParseHex(rawValue.AsSpan(2), out var bytes))
                return (bytes, RegistryValueType.Binary);
            // Malformed hex — keep the raw text rather than silently dropping the value.
            return (rawValue, RegistryValueType.String);
        }

        if (rawValue.StartsWith("#%", StringComparison.Ordinal))
            return (rawValue[2..], RegistryValueType.ExpandString);

        if (rawValue.StartsWith("##", StringComparison.Ordinal))
            return (rawValue[1..], RegistryValueType.String);

        if (rawValue.StartsWith('#'))
        {
            if (int.TryParse(rawValue.AsSpan(1), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var intVal))
                return (intVal, RegistryValueType.DWord);
            // A leading '#' that is neither a type prefix nor a decimal DWORD — treat as literal text.
            return (rawValue, RegistryValueType.String);
        }

        return (rawValue, RegistryValueType.String);
    }

    /// <summary>
    /// Inverts <c>RegistryTableProducer.EncodeMultiString</c>: <c>[~]</c> (empty list),
    /// <c>[~]value[~]</c> (single element, the producer's wrap form), and <c>a[~]b[~]c</c>
    /// (two or more elements, a plain <c>[~]</c>-join). Interior and edge empty elements of the
    /// join form are preserved — splitting without <see cref="StringSplitOptions.RemoveEmptyEntries"/> —
    /// so a decompile→recompile reproduces the same <c>Registry.Value</c> string. The wrap form
    /// <c>[~]value[~]</c> is decoded to the single element <c>[value]</c>; it is inherently ambiguous
    /// with the non-canonical three-element <c>["", value, ""]</c> because the producer's encoding is
    /// not injective, so the producer's own single-element form is chosen.
    /// </summary>
    private static List<string> DecodeMultiString(string rawValue)
    {
        const string separator = "[~]";

        if (rawValue == separator)
            return [];

        // Keep empty segments — an empty segment can be a genuine element of the join form.
        string[] parts = rawValue.Split(separator);

        // The single-element wrap form "[~]value[~]" splits to ["", value, ""]; collapse it back.
        if (parts.Length == 3 && parts[0].Length == 0 && parts[2].Length == 0)
            return [parts[1]];

        return [.. parts];
    }

    /// <summary>
    /// Parses the hex payload of a <c>#x</c> REG_BINARY value. Requires an even length and
    /// only ASCII hex digits (the producer writes <see cref="Convert.ToHexString(byte[])"/>);
    /// returns <see langword="false"/> for anything malformed so the caller can preserve the raw text.
    /// </summary>
    private static bool TryParseHex(ReadOnlySpan<char> hex, out byte[] bytes)
    {
        if ((hex.Length & 1) != 0)
        {
            bytes = [];
            return false;
        }

        foreach (char c in hex)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                bytes = [];
                return false;
            }
        }

        bytes = Convert.FromHexString(hex);
        return true;
    }
}
