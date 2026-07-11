using System.Runtime.Versioning;
using FalkForge.Extensibility;
using FalkForge.Extensions.Iis;
using FalkForge.Extensions.Iis.Models;
using FalkForge.Extensions.Sql;
using FalkForge.Extensions.Util;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

/// <summary>
/// Regression guard for the seam bug where every secret-bearing extension authored its OWN
/// <c>MsiHiddenProperties</c> <c>Property</c> row. Two or more such extensions in one package (SQL +
/// IIS + Util user/group — exactly the AcmeSuite enterprise composition) produced two-plus rows with the
/// same <c>Property</c> primary key (<c>"MsiHiddenProperties"</c>) and the build failed on a duplicate PK.
///
/// <para>The fix aggregates the secret property names declared by every execution step across ALL
/// extensions into a SINGLE deterministic <c>MsiHiddenProperties</c> row. This test proves (a) the
/// multi-secret package now BUILDS, (b) exactly ONE such row exists, (c) its semicolon-delimited value
/// contains every one of the three extensions' secret property names — a dropped name would leak that
/// password into a verbose <c>msiexec /L*v</c> log — and (d) the list is deterministically sorted.</para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MultiSecretHiddenPropertiesAggregationTests
{
    [Fact]
    public void SqlIisAndUtilSecrets_Aggregate_IntoSingleSortedMsiHiddenPropertiesRow()
    {
        using var scratch = new Scratch();

        // SQL database with SQL-auth credentials → secret carried via the CustomActionData channel.
        var sql = new SqlExtension();
        var dbRef = sql.DefineDatabase(db => db
            .Id("AppDb").Server(".").Database("AcmeDb").CreateOnInstall()
            .User("appLogin").PasswordProperty("SQLPASSWORD"));
        Assert.True(dbRef.IsSuccess, dbRef.IsFailure ? dbRef.Error.Message : "");

        // IIS SpecificUser app pool with a secure password property.
        var iis = new IisExtension();
        iis.AddAppPool(p => p
            .Id("SecurePool").Name("SecurePool")
            .IdentitySecure(AppPoolIdentityType.SpecificUser, "domain\\svc", "IISAPPPOOLPWD"));

        // Util local user created with a secure password property.
        var util = new UtilExtension();
        Assert.True(util.AddUser(u => u.Name("svcAcme").PasswordProperty("USERPASSWORD")).IsSuccess);

        // On main this Compile fails with a duplicate Property primary-key error (three rows all keyed
        // "MsiHiddenProperties"). After the fix it must succeed.
        using var db = Compile(scratch, "AcmeMultiSecretApp", sql, iis, util);

        var hidden = db.QueryRows(
            "SELECT `Value` FROM `Property` WHERE `Property`='MsiHiddenProperties'", 1);
        Assert.True(hidden.IsSuccess, hidden.IsFailure ? hidden.Error.Message : "");

        // Exactly ONE aggregated row — never one-per-extension.
        string?[] row = Assert.Single(hidden.Value);
        string value = row[0] ?? "";

        string[] names = value.Split(';', StringSplitOptions.RemoveEmptyEntries);

        // Every extension's secret property names survive aggregation (no dropped secret → no leaked password).
        Assert.Contains("SQLPASSWORD", names);        // SQL secure source property
        Assert.Contains("SqlDb_AppDb", names);        // SQL deferred action's CustomActionData property
        Assert.Contains("IISAPPPOOLPWD", names);      // IIS secure source property
        Assert.Contains("IisPool_SecurePool", names); // IIS deferred action's CustomActionData property
        Assert.Contains("USERPASSWORD", names);       // Util secure source property
        Assert.Contains("UUsr_svcAcme", names);       // Util deferred action's CustomActionData property

        // Deterministically ordered (ordinal-sorted) for reproducible builds.
        string[] sorted = [.. names.OrderBy(n => n, StringComparer.Ordinal)];
        Assert.Equal(sorted, names);
    }

    private static MsiDatabase Compile(Scratch scratch, string name, params IFalkForgeExtension[] extensions)
    {
        var package = MinimalPackage(scratch, name);
        var compiler = new MsiCompiler(new WindowsFileSystem());
        var result = compiler.Use(extensions).Compile(package, scratch.OutputDir);
        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

        var dbResult = MsiDatabase.Open(result.Value, readOnly: true);
        Assert.True(dbResult.IsSuccess, $"Open failed: {(dbResult.IsFailure ? dbResult.Error.Message : "")}");
        return dbResult.Value;
    }

    private static PackageModel MinimalPackage(Scratch scratch, string name)
    {
        var sourceFile = Path.Combine(scratch.SourceDir, "app.exe");
        File.WriteAllText(sourceFile, "payload for multi-secret aggregation test");

        return InstallerTestHost.BuildPackage(p =>
        {
            p.Name = name;
            p.Manufacturer = "Corp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "Corp" / name));
        });
    }

    private sealed class Scratch : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"MultiSecretEmit_{Guid.NewGuid():N}");

        public Scratch()
        {
            SourceDir = Path.Combine(_root, "source");
            OutputDir = Path.Combine(_root, "output");
            Directory.CreateDirectory(SourceDir);
            Directory.CreateDirectory(OutputDir);
        }

        public string SourceDir { get; }
        public string OutputDir { get; }

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }
}
