using Microsoft.Data.SqlClient;
using Xunit;

namespace FalkForge.Plugins.Sql.Tests;

public sealed class ConnectionStringHelperTests
{
    // ── Encrypt mutant: encrypt ? Mandatory : Optional ──────────────────────

    [Fact]
    public void Build_encrypt_true_sets_Mandatory()
    {
        var cs = ConnectionStringHelper.Build(
            server: "localhost",
            database: null,
            integratedSecurity: true,
            userName: null,
            password: null,
            encrypt: true);

        var parsed = new SqlConnectionStringBuilder(cs);
        Assert.Equal(SqlConnectionEncryptOption.Mandatory, parsed.Encrypt);
    }

    [Fact]
    public void Build_encrypt_false_sets_Optional()
    {
        var cs = ConnectionStringHelper.Build(
            server: "localhost",
            database: null,
            integratedSecurity: true,
            userName: null,
            password: null,
            encrypt: false);

        var parsed = new SqlConnectionStringBuilder(cs);
        Assert.Equal(SqlConnectionEncryptOption.Optional, parsed.Encrypt);
    }

    // ── Database mutant: !IsNullOrEmpty(database) inverted ──────────────────

    [Fact]
    public void Build_with_database_sets_InitialCatalog()
    {
        var cs = ConnectionStringHelper.Build(
            server: "localhost",
            database: "mydb",
            integratedSecurity: true,
            userName: null,
            password: null);

        var parsed = new SqlConnectionStringBuilder(cs);
        Assert.Equal("mydb", parsed.InitialCatalog);
    }

    [Fact]
    public void Build_with_null_database_leaves_InitialCatalog_empty()
    {
        var cs = ConnectionStringHelper.Build(
            server: "localhost",
            database: null,
            integratedSecurity: true,
            userName: null,
            password: null);

        var parsed = new SqlConnectionStringBuilder(cs);
        Assert.Equal(string.Empty, parsed.InitialCatalog);
    }

    [Fact]
    public void Build_with_empty_database_leaves_InitialCatalog_empty()
    {
        var cs = ConnectionStringHelper.Build(
            server: "localhost",
            database: string.Empty,
            integratedSecurity: true,
            userName: null,
            password: null);

        var parsed = new SqlConnectionStringBuilder(cs);
        Assert.Equal(string.Empty, parsed.InitialCatalog);
    }

    // ── IntegratedSecurity mutant: !integratedSecurity inverted ─────────────

    [Fact]
    public void Build_integratedSecurity_false_includes_credentials()
    {
        var cs = ConnectionStringHelper.Build(
            server: "localhost",
            database: null,
            integratedSecurity: false,
            userName: "sa",
            password: "secret");

        var parsed = new SqlConnectionStringBuilder(cs);
        Assert.Equal("sa", parsed.UserID);
        Assert.Equal("secret", parsed.Password);
    }

    [Fact]
    public void Build_integratedSecurity_false_null_credentials_uses_empty_string()
    {
        // Kills the ?? string.Empty null-coalescing mutations on UserID and Password (lines 25-26).
        var cs = ConnectionStringHelper.Build(
            server: "localhost",
            database: null,
            integratedSecurity: false,
            userName: null,
            password: null);

        var parsed = new SqlConnectionStringBuilder(cs);
        Assert.Equal(string.Empty, parsed.UserID);
        Assert.Equal(string.Empty, parsed.Password);
    }

    [Fact]
    public void Build_integratedSecurity_true_omits_credentials()
    {
        var cs = ConnectionStringHelper.Build(
            server: "localhost",
            database: null,
            integratedSecurity: true,
            userName: "sa",
            password: "secret");

        var parsed = new SqlConnectionStringBuilder(cs);
        Assert.Equal(string.Empty, parsed.UserID);
        Assert.Equal(string.Empty, parsed.Password);
    }

    // ── Baseline / other fields ──────────────────────────────────────────────

    [Fact]
    public void Build_sets_DataSource()
    {
        var cs = ConnectionStringHelper.Build(
            server: "myserver\\SQLEXPRESS",
            database: null,
            integratedSecurity: true,
            userName: null,
            password: null);

        var parsed = new SqlConnectionStringBuilder(cs);
        Assert.Equal(@"myserver\SQLEXPRESS", parsed.DataSource);
    }

    [Fact]
    public void Build_default_timeout_is_5_seconds()
    {
        var cs = ConnectionStringHelper.Build(
            server: "localhost",
            database: null,
            integratedSecurity: true,
            userName: null,
            password: null);

        var parsed = new SqlConnectionStringBuilder(cs);
        Assert.Equal(5, parsed.ConnectTimeout);
    }

    [Fact]
    public void Build_custom_timeout_is_applied()
    {
        var cs = ConnectionStringHelper.Build(
            server: "localhost",
            database: null,
            integratedSecurity: true,
            userName: null,
            password: null,
            timeoutSeconds: 30);

        var parsed = new SqlConnectionStringBuilder(cs);
        Assert.Equal(30, parsed.ConnectTimeout);
    }

    [Fact]
    public void Build_trustServerCertificate_true_is_applied()
    {
        var cs = ConnectionStringHelper.Build(
            server: "localhost",
            database: null,
            integratedSecurity: true,
            userName: null,
            password: null,
            trustServerCertificate: true);

        var parsed = new SqlConnectionStringBuilder(cs);
        Assert.True(parsed.TrustServerCertificate);
    }
}
