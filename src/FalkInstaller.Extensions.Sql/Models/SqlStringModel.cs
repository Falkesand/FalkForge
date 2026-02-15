namespace FalkInstaller.Extensions.Sql.Models;

public sealed class SqlStringModel
{
    public required string Id { get; init; }
    public required string DatabaseRef { get; init; }
    public required string Sql { get; init; }
    public bool ExecuteOnInstall { get; init; }
    public bool ExecuteOnUninstall { get; init; }
    public int Sequence { get; init; }
    public bool ContinueOnError { get; init; }
}
