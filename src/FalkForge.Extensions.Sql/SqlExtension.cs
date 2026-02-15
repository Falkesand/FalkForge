using FalkForge.Extensibility;
using FalkForge.Extensions.Sql.Models;

namespace FalkForge.Extensions.Sql;

public sealed class SqlExtension : IFalkForgeExtension
{
    private readonly SqlDatabaseTableContributor _databaseContributor = new();
    private readonly SqlScriptTableContributor _scriptContributor = new();
    private readonly SqlStringTableContributor _stringContributor = new();

    public string Name => "Sql";

    public SqlDatabaseTableContributor Databases => _databaseContributor;
    public SqlScriptTableContributor Scripts => _scriptContributor;
    public SqlStringTableContributor Strings => _stringContributor;

    public void Register(IExtensionRegistry registry)
    {
        registry.RegisterTableContributor(_databaseContributor);
        registry.RegisterTableContributor(_scriptContributor);
        registry.RegisterTableContributor(_stringContributor);
    }
}
