namespace FalkForge.Extensions.Sql;

/// <summary>
/// Raw row read from the MSI <c>SqlString</c> table by the decompile pipeline.
/// Column order mirrors the write-side <see cref="SqlStringTableContributor.GetRows"/> output.
/// </summary>
public sealed record SqlStringRow(
    string  Id,
    string  Database_,
    string  Sql,
    bool    ExecuteOnInstall,
    bool    ExecuteOnUninstall,
    int     Sequence,
    bool    ContinueOnError);
