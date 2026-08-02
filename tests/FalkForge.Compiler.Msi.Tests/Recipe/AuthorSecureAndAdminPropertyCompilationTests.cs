using System.Runtime.Versioning;
using FalkForge.Builders;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

/// <summary>
/// Real compiled-MSI coverage for <see cref="PropertyModel.IsSecure"/> and
/// <see cref="PropertyModel.IsAdmin"/>. Every prior test for these two flags called
/// <c>PropertyTableProducer.Produce</c> in isolation — the bug class this branch exists to catch
/// ("producer says yes, compiled MSI says no") needs at least one assertion against a database
/// actually opened with <see cref="MsiDatabase.Open"/>, the same shape <see cref="PropertyModel.IsHidden"/>
/// already has in <c>AuthorHiddenPropertyMergeTests</c>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AuthorSecureAndAdminPropertyCompilationTests
{
    [Fact]
    public void AuthorSecureProperty_IsListedInCompiledSecureCustomProperties()
    {
        using var scratch = new Scratch();

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "SecurePropertyApp";
            p.Manufacturer = "Corp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(WriteSourceFile(scratch, "app.exe")).To(KnownFolder.ProgramFiles / "Corp" / "SecurePropertyApp"));
            p.Property("APP_SECRET", "x", cfg => cfg.IsSecure = true);
        });

        using var db = Compile(scratch, package);

        Assert.Equal("APP_SECRET", SinglePropertyListValue(db, "SecureCustomProperties"));
    }

    [Fact]
    public void AuthorAdminProperty_IsListedInCompiledAdminProperties()
    {
        using var scratch = new Scratch();

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "AdminPropertyApp";
            p.Manufacturer = "Corp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(WriteSourceFile(scratch, "app.exe")).To(KnownFolder.ProgramFiles / "Corp" / "AdminPropertyApp"));
            p.Property("DEPLOY_TIER", "prod", cfg => cfg.IsAdmin = true);
        });

        using var db = Compile(scratch, package);

        Assert.Equal("DEPLOY_TIER", SinglePropertyListValue(db, "AdminProperties"));
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string WriteSourceFile(Scratch scratch, string fileName)
    {
        var sourceFile = Path.Combine(scratch.SourceDir, fileName);
        File.WriteAllText(sourceFile, "payload for author secure/admin property compilation test");
        return sourceFile;
    }

    private static string SinglePropertyListValue(MsiDatabase db, string listPropertyName)
    {
        var rows = db.QueryRows(
            $"SELECT `Value` FROM `Property` WHERE `Property`='{listPropertyName}'", 1);
        Assert.True(rows.IsSuccess, rows.IsFailure ? rows.Error.Message : "");
        return Assert.Single(rows.Value.Select(row => row[0] ?? ""));
    }

    private static MsiDatabase Compile(Scratch scratch, PackageModel package)
    {
        var compiler = new MsiCompiler(new WindowsFileSystem());
        var result = compiler.Compile(package, scratch.OutputDir);
        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

        var dbResult = MsiDatabase.Open(result.Value, readOnly: true);
        Assert.True(dbResult.IsSuccess, $"Open failed: {(dbResult.IsFailure ? dbResult.Error.Message : "")}");
        return dbResult.Value;
    }

    private sealed class Scratch : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"AuthorSecureAdminProp_{Guid.NewGuid():N}");

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
            TestTemp.TryDelete(_root);
        }
    }
}
