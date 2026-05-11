namespace FalkForge.Extensions.Sql;

/// <summary>
/// Raw row read from the MSI <c>SqlDatabase</c> table by the decompile pipeline.
/// Column order mirrors the write-side <see cref="SqlDatabaseTableContributor.GetRows"/> output.
/// </summary>
public sealed record SqlDatabaseRow(
    string  Id,
    string? Server,
    string  Database,
    string? Instance,
    string? ConnectionString,
    bool    CreateOnInstall,
    bool    DropOnUninstall,
    bool    ConfirmOverwrite,
    string? Component_);
