using System.Collections.Immutable;

namespace FalkForge.Decompiler.Recipe.Schemas;

/// <summary>
/// Raw row returned by <see cref="FileSchema.Schema"/>.
/// </summary>
public sealed record FileRow(
    string  File,
    string  Component_,
    string  FileName,
    int     FileSize,
    string? Version,
    string? Language,
    int     Attributes,
    int     Sequence);

/// <summary>
/// Declarative read schema for the MSI <c>File</c> table.
/// Columns: File (PK), Component_, FileName, FileSize, Version, Language, Attributes, Sequence.
/// </summary>
public static class FileSchema
{
    public static readonly ReadColumn File       = new("File",       ReadColumnType.String,  false, 0);
    public static readonly ReadColumn Component_ = new("Component_", ReadColumnType.String,  false, 1);
    public static readonly ReadColumn FileName   = new("FileName",   ReadColumnType.String,  false, 2);
    public static readonly ReadColumn FileSize   = new("FileSize",   ReadColumnType.Integer, false, 3);
    public static readonly ReadColumn Version    = new("Version",    ReadColumnType.String,  true,  4);
    public static readonly ReadColumn Language   = new("Language",   ReadColumnType.String,  true,  5);
    public static readonly ReadColumn Attributes = new("Attributes", ReadColumnType.Integer, false, 6);
    public static readonly ReadColumn Sequence   = new("Sequence",   ReadColumnType.Integer, false, 7);

    public static readonly TableReadSchema<FileRow> Schema = new(
        TableName: "File",
        Columns: [File, Component_, FileName, FileSize, Version, Language, Attributes, Sequence],
        Map: row => Result<FileRow>.Success(new FileRow(
            row.String(File),
            row.String(Component_),
            row.String(FileName),
            row.Int32(FileSize),
            row.StringOrNull(Version),
            row.StringOrNull(Language),
            row.Int32(Attributes),
            row.Int32(Sequence))));
}
