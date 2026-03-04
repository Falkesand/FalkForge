using FalkForge.Extensibility;
using FalkForge.Extensions.Sql.Builders;

namespace FalkForge.Extensions.Sql;

public sealed class SqlExtension : IFalkForgeExtension
{
    public SqlDatabaseTableContributor Databases { get; } = new();

    public SqlScriptTableContributor Scripts { get; } = new();

    public SqlStringTableContributor Strings { get; } = new();

    public string Name => "Sql";

    public void Register(IExtensionRegistry registry)
    {
        registry.RegisterTableContributor(Databases);
        registry.RegisterTableContributor(Scripts);
        registry.RegisterTableContributor(Strings);
    }

    public Result<SqlDatabaseRef> DefineDatabase(Action<SqlDatabaseBuilder> configure)
    {
        var builder = new SqlDatabaseBuilder();
        configure(builder);
        var result = builder.Build();
        if (result.IsFailure)
            return Result<SqlDatabaseRef>.Failure(result.Error);

        Databases.Add(result.Value);
        return new SqlDatabaseRef(result.Value.Id);
    }
}