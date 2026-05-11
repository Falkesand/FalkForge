namespace FalkForge.Extensions.Sql;

/// <summary>
/// Raw row read from the MSI <c>SqlScript</c> table by the decompile pipeline.
/// Column order mirrors the write-side <see cref="SqlScriptTableContributor.GetRows"/> output.
/// </summary>
public sealed record SqlScriptRow(
    string  Id,
    string  Database_,
    string? SourceFile,
    string? SqlContent,
    bool    ExecuteOnInstall,
    bool    ExecuteOnReinstall,
    bool    ExecuteOnUninstall,
    string? RollbackSourceFile,
    int     Sequence,
    bool    ContinueOnError,
    string? Component_);
