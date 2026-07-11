using System.Linq;
using FalkForge.Extensions.Sql.Models;
using FalkForge.Models;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Extensions.Sql.Tests;

/// <summary>
/// Proves the compile-time SQL security warnings fire through the validation pipeline — most importantly
/// SQL015, which flags a literal SQL password that would be embedded in plaintext in the MSI.
/// </summary>
public sealed class SqlRulesTests
{
    [Fact]
    public void Sql015_Fires_WhenLiteralPasswordWouldBeEmbedded()
    {
        var db = new SqlDatabaseModel
        {
            Id = "Db", Server = "svr", Database = "AppDb", User = "appLogin", Password = "s3cr3t",
        };

        var violations = Evaluate("SQL015", [db]);

        var v = Assert.Single(violations);
        Assert.Equal(Severity.Warning, v.Severity);
    }

    [Fact]
    public void Sql015_DoesNotFire_ForSecureProperty()
    {
        var db = new SqlDatabaseModel
        {
            Id = "Db", Server = "svr", Database = "AppDb", User = "appLogin", PasswordProperty = "SQLPASSWORD",
        };

        Assert.Empty(Evaluate("SQL015", [db]));
    }

    [Fact]
    public void Sql015_DoesNotFire_ForIntegratedAuth()
    {
        var db = new SqlDatabaseModel { Id = "Db", Server = "svr", Database = "AppDb" };

        Assert.Empty(Evaluate("SQL015", [db]));
    }

    private static List<Violation> Evaluate(string ruleId, IReadOnlyList<SqlDatabaseModel> databases)
    {
        ValidationRule rule = SqlRules.Build(() => databases, () => [], () => [])
            .Single(r => r.Id.Value == ruleId);
        RuleContext ctx = RuleContext.ForTest(new PackageModel
        {
            Name = "Test", Manufacturer = "Corp", Version = new Version(1, 0, 0),
        });
        return rule.Evaluate(ctx).ToList();
    }
}
