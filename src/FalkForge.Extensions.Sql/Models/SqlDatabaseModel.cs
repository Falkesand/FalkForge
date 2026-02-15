namespace FalkForge.Extensions.Sql.Models;

public sealed class SqlDatabaseModel
{
    public required string Id { get; init; }
    public string? Server { get; init; }
    public required string Database { get; init; }
    public string? Instance { get; init; }
    public string? ConnectionString { get; init; }
    public bool CreateOnInstall { get; init; }
    public bool DropOnUninstall { get; init; }
    public bool ConfirmOverwrite { get; init; }
    public string? ComponentRef { get; init; }
}
