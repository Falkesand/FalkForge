using System.Collections.Immutable;
using FalkForge.Extensibility;
using FalkForge.Extensions.Sql.Builders;
using FalkForge.Validation;

namespace FalkForge.Extensions.Sql;

public sealed class SqlExtension : IFalkForgeExtension, IDryRunContributor
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
        // Scrub SQL passwords carried through the CustomActionData channel from verbose MSI logs.
        registry.RegisterTableContributor(new SqlHiddenPropertiesContributor(
            () => Databases.Items, () => Scripts.Items, () => Strings.Items));
        // Make the SqlDatabase/SqlScript/SqlString tables LIVE: schedule deferred, elevated custom actions
        // that create databases, run scripts/strings, and drop databases on uninstall.
        registry.RegisterExecutionContributor(new SqlExecutionContributor(
            () => Databases.Items, () => Scripts.Items, () => Strings.Items));
    }

    /// <inheritdoc/>
    public ImmutableArray<ValidationRule> GetValidationRules()
        => SqlRules.Build(() => Databases.Items, () => Scripts.Items, () => Strings.Items);

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

    public IReadOnlyList<DryRunAction> GetDryRunActions(DryRunIntent intent) =>
        intent switch
        {
            DryRunIntent.Install =>
            [
                new DryRunAction { Kind = DryRunActionKind.Database, Description = "Would create SQL Server database(s)" },
                new DryRunAction { Kind = DryRunActionKind.Database, Description = "Would execute SQL script(s)" }
            ],
            DryRunIntent.Uninstall => [new DryRunAction { Kind = DryRunActionKind.Database, Description = "Would drop SQL Server database(s)" }],
            _ => []
        };
}
