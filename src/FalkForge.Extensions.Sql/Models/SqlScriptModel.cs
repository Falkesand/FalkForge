namespace FalkForge.Extensions.Sql.Models;

public sealed class SqlScriptModel
{
    public required string Id { get; init; }
    public required string DatabaseRef { get; init; }
    public string? SourceFile { get; init; }
    public string? SqlContent { get; init; }
    public bool ExecuteOnInstall { get; init; }
    public bool ExecuteOnReinstall { get; init; }
    public bool ExecuteOnUninstall { get; init; }
    public string? RollbackSourceFile { get; init; }
    public int Sequence { get; init; }
    public bool ContinueOnError { get; init; }
    public string? ComponentRef { get; init; }
}
