using System.Runtime.Versioning;
using FalkForge.Builders;
using FalkForge.Extensibility;
using FalkForge.Extensions.Sql;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

/// <summary>
/// Proves the STEP 5 merge: an author property flagged <see cref="PropertyModel.IsHidden"/> and an
/// extension-contributed secret (<see cref="ExecutionStep.HiddenProperties"/>) land in the SAME
/// single <c>MsiHiddenProperties</c> row, via <c>HiddenPropertiesEmitter.TryBuild</c> called
/// unconditionally from <c>MsiRecipeBuilder.ApplyExtensionContributors</c> — not only when an
/// execution-contributing extension is present. Before this change, <c>PropertyModel.IsHidden</c>
/// was stored and read by nothing: a property an author marked hidden was written to a verbose
/// <c>msiexec /L*v</c> install log in plaintext with no warning and no error.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AuthorHiddenPropertyMergeTests
{
    [Fact]
    public void AuthorHiddenProperty_AndExtensionSecret_MergeIntoOneSortedRow()
    {
        using var scratch = new Scratch();

        var sql = new SqlExtension();
        var dbRef = sql.DefineDatabase(db => db
            .Id("AppDb").Server(".").Database("AcmeDb").CreateOnInstall()
            .User("appLogin").PasswordProperty("SQLPASSWORD"));
        Assert.True(dbRef.IsSuccess, dbRef.IsFailure ? dbRef.Error.Message : "");

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "AuthorHiddenMergeApp";
            p.Manufacturer = "Corp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(WriteSourceFile(scratch, "app.exe")).To(KnownFolder.ProgramFiles / "Corp" / "AuthorHiddenMergeApp"));
            p.Property("APP_HIDDEN_SECRET", "x", cfg => cfg.IsHidden = true);
        });

        using var db = Compile(scratch, package, sql);

        string hiddenValue = SingleHiddenPropertiesValue(db);
        string[] names = hiddenValue.Split(';', StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("APP_HIDDEN_SECRET", names);
        Assert.Contains("SQLPASSWORD", names);
        Assert.Contains("SqlDb_AppDb", names);

        string[] sorted = [.. names.OrderBy(n => n, StringComparer.Ordinal)];
        Assert.Equal(sorted, names);
    }

    [Fact]
    public void AuthorHiddenProperty_WithNoExtensionsAtAll_StillEmitsTheRow()
    {
        using var scratch = new Scratch();

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "AuthorHiddenOnlyApp";
            p.Manufacturer = "Corp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(WriteSourceFile(scratch, "app.exe")).To(KnownFolder.ProgramFiles / "Corp" / "AuthorHiddenOnlyApp"));
            p.Property("APP_HIDDEN_SECRET", "x", cfg => cfg.IsHidden = true);
        });

        // No extensions attached at all — the gate this test exists to catch was
        // `if (steps.Count > 0)`, which an author-hidden-only package never reaches.
        using var db = Compile(scratch, package);

        Assert.Equal("APP_HIDDEN_SECRET", SingleHiddenPropertiesValue(db));
    }

    [Fact]
    public void NothingHiddenAnywhere_EmitsNoMsiHiddenPropertiesRow()
    {
        using var scratch = new Scratch();

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "NothingHiddenApp";
            p.Manufacturer = "Corp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(WriteSourceFile(scratch, "app.exe")).To(KnownFolder.ProgramFiles / "Corp" / "NothingHiddenApp"));
            p.Property("APP_PLAIN", "x");
        });

        using var db = Compile(scratch, package);

        Assert.Empty(HiddenPropertiesRows(db));
    }

    [Fact]
    public void UnflaggedProperty_IsAbsentFromTheAggregatedValue()
    {
        using var scratch = new Scratch();

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "MixedFlagsApp";
            p.Manufacturer = "Corp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(WriteSourceFile(scratch, "app.exe")).To(KnownFolder.ProgramFiles / "Corp" / "MixedFlagsApp"));
            p.Property("APP_HIDDEN_SECRET", "x", cfg => cfg.IsHidden = true);
            p.Property("APP_PLAIN", "y");
        });

        using var db = Compile(scratch, package);

        Assert.Equal("APP_HIDDEN_SECRET", SingleHiddenPropertiesValue(db));
    }

    [Fact]
    public void MixedCaseName_FlaggedHidden_IsEmitted()
    {
        // Pins that PRP001's uppercase check (IsSecure only) has NOT crept onto IsHidden — MSI's
        // MsiHiddenProperties has no casing rule, so a mixed-case hidden name must still be honored.
        using var scratch = new Scratch();

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "MixedCaseHiddenApp";
            p.Manufacturer = "Corp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(WriteSourceFile(scratch, "app.exe")).To(KnownFolder.ProgramFiles / "Corp" / "MixedCaseHiddenApp"));
            p.Property("dbPassword", "x", cfg => cfg.IsHidden = true);
        });

        using var db = Compile(scratch, package);

        Assert.Equal("dbPassword", SingleHiddenPropertiesValue(db));
    }

    [Fact]
    public void ExtensionSecretsOnly_StillEmitsOneRow()
    {
        // Regression guard on the refactor: with no author-hidden properties at all, the
        // extension-only path (formerly ExecutionStepEmitter's own row emission, now merged via
        // HiddenPropertiesEmitter) must still behave exactly as before.
        using var scratch = new Scratch();

        var sql = new SqlExtension();
        var dbRef = sql.DefineDatabase(db => db
            .Id("AppDb").Server(".").Database("AcmeDb").CreateOnInstall()
            .User("appLogin").PasswordProperty("SQLPASSWORD"));
        Assert.True(dbRef.IsSuccess, dbRef.IsFailure ? dbRef.Error.Message : "");

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "ExtensionOnlyApp";
            p.Manufacturer = "Corp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(WriteSourceFile(scratch, "app.exe")).To(KnownFolder.ProgramFiles / "Corp" / "ExtensionOnlyApp"));
        });

        using var db2 = Compile(scratch, package, sql);

        string hiddenValue = SingleHiddenPropertiesValue(db2);
        string[] names = hiddenValue.Split(';', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("SQLPASSWORD", names);
        Assert.Contains("SqlDb_AppDb", names);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string WriteSourceFile(Scratch scratch, string fileName)
    {
        var sourceFile = Path.Combine(scratch.SourceDir, fileName);
        File.WriteAllText(sourceFile, "payload for author-hidden-property merge test");
        return sourceFile;
    }

    private static string[] HiddenPropertiesRows(MsiDatabase db)
    {
        var hidden = db.QueryRows(
            "SELECT `Value` FROM `Property` WHERE `Property`='MsiHiddenProperties'", 1);
        Assert.True(hidden.IsSuccess, hidden.IsFailure ? hidden.Error.Message : "");
        return [.. hidden.Value.Select(row => row[0] ?? "")];
    }

    private static string SingleHiddenPropertiesValue(MsiDatabase db)
        => Assert.Single(HiddenPropertiesRows(db));

    private static MsiDatabase Compile(Scratch scratch, PackageModel package, params IFalkForgeExtension[] extensions)
    {
        var compiler = new MsiCompiler(new WindowsFileSystem());
        var result = compiler.Use(extensions).Compile(package, scratch.OutputDir);
        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

        var dbResult = MsiDatabase.Open(result.Value, readOnly: true);
        Assert.True(dbResult.IsSuccess, $"Open failed: {(dbResult.IsFailure ? dbResult.Error.Message : "")}");
        return dbResult.Value;
    }

    private sealed class Scratch : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"AuthorHiddenMerge_{Guid.NewGuid():N}");

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
