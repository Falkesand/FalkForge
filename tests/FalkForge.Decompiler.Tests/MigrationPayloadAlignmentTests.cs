using System.Runtime.Versioning;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using Xunit;

namespace FalkForge.Decompiler.Tests;

/// <summary>
/// End-to-end alignment invariant for "forge migrate" payload handling.
///
/// WHY this matters:
/// The generated Program.cs calls files.Add("payload/...") and the migration also
/// extracts the payload bytes into MigrationResult.Payloads keyed by the same string.
/// If those two keys diverge, the migrated project either references a payload file
/// that was never written, or writes a payload file that the code never adds — a
/// silently broken migration. This test compiles a real one-file MSI, runs the real
/// migration generator, and asserts every extracted payload key appears verbatim as
/// an .Add("&lt;key&gt;") call in the generated Program.cs. Alignment must hold by
/// construction (both sides derive from PayloadPath.For + FindRootFolder).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MigrationPayloadAlignmentTests
{
    [Fact]
    public void Generate_RealMsi_PayloadKeysAppearAsAddCallsInProgramCs()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"falk-migrate-align-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            // Arrange: author a one-file MSI installing into ProgramFiles/<sub>.
            var payloadPath = Path.Combine(tempRoot, "app.exe");
            File.WriteAllBytes(payloadPath, [0x4D, 0x5A, 0x90, 0x00]); // tiny stub bytes

            var builder = new PackageBuilder
            {
                Name = "AlignApp",
                Manufacturer = "Align Corp",
                Version = new Version(1, 0, 0),
            };
            builder.Files(f => f
                .Add(payloadPath)
                .To(KnownFolder.ProgramFiles / "AlignApp"));

            var model = builder.Build();
            var compileResult = new MsiCompiler().Compile(model, tempRoot);
            Assert.True(compileResult.IsSuccess,
                compileResult.IsFailure ? compileResult.Error.Message : "");
            var msiPath = compileResult.Value;

            // Act: run the REAL generator (opens the MSI database, extracts cab payloads).
            var options = new MigrationOptions(FalkForgeSourcePath: "../../src", ProjectName: "Aligned");
            var result = new MigrationProjectGenerator().Generate(msiPath, options);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");

            var migration = result.Value;
            var programCs = migration.TextFiles["Program.cs"];

            // Assert: payloads were extracted, and every key aligns with an .Add(...) call.
            Assert.NotEmpty(migration.Payloads);
            foreach (var key in migration.Payloads.Keys)
                Assert.Contains($".Add(\"{key}\")", programCs);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch (IOException) { }
        }
    }
}
