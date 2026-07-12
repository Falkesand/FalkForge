using System.Runtime.Versioning;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

/// <summary>
/// End-to-end proof that authoring an environment variable with the fluent
/// <c>Append</c>/<c>Prepend</c> part semantics drives the compiled MSI: the real
/// <c>Environment</c> table <c>Value</c> cell must carry the <c>[~]</c> token in the
/// correct position so Windows Installer splices the new text onto the *existing*
/// value at install time rather than overwriting it.
///
/// The direction is the load-bearing detail and is easy to invert, so it is asserted
/// against the real MSI, not just a unit-level string:
///   append  (add to the end)   -> <c>[~];NEW</c>  (existing value first, then separator, then NEW)
///   prepend (add to the front) -> <c>NEW;[~]</c>  (NEW first, then separator, then existing value)
/// This matches the WiX <c>Environment/@Part</c> ("last"/"first") behaviour and the MSI
/// SDK "Environment Table" topic.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EnvironmentPartCompiledMsiTests
{
    [Fact]
    public void Compile_AppendAndPrependEnvVars_EmitTildeTokenInCorrectPosition()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"EnvPart_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceFile = Path.Combine(tempDir, "app.exe");
            File.WriteAllText(sourceFile, "fake exe for environment part test");

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "EnvPart";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(1, 0, 0);
                p.Feature("Main", f =>
                {
                    f.Files(fs => fs.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "EnvPart"));
                    f.EnvironmentVariable("FF_APPEND", @"C:\App\bin", e => e.Append());
                    f.EnvironmentVariable("FF_PREPEND", @"C:\App\lib", e => e.Prepend());
                    f.EnvironmentVariable("FF_CUSTOMSEP", @"C:\App\py", e => e.Append(":"));
                });
            });

            var fileSystem = new WindowsFileSystem();
            var compiler = new MsiCompiler(fileSystem);
            var compileResult = compiler.Compile(package, outputDir);
            Assert.True(compileResult.IsSuccess,
                $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

            using var db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;
            var rows = db.QueryRows("SELECT `Name`, `Value` FROM `Environment`", 2).Value;

            // Append: existing value first ([~]) then default separator then the new text.
            var appendRow = Assert.Single(rows, r => r[0]!.Contains("FF_APPEND"));
            Assert.Equal(@"[~];C:\App\bin", appendRow[1]);

            // Prepend: new text first then default separator then the existing value ([~]).
            var prependRow = Assert.Single(rows, r => r[0]!.Contains("FF_PREPEND"));
            Assert.Equal(@"C:\App\lib;[~]", prependRow[1]);

            // Custom separator flows through to the Value encoding.
            var customSepRow = Assert.Single(rows, r => r[0]!.Contains("FF_CUSTOMSEP"));
            Assert.Equal(@"[~]:C:\App\py", customSepRow[1]);

            // The Name column must NOT carry the '=' set/overwrite prefix for append/prepend —
            // '=' makes Windows Installer ignore the [~] token and clobber the existing value.
            Assert.DoesNotContain('=', appendRow[0]!);
            Assert.DoesNotContain('=', prependRow[0]!);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
