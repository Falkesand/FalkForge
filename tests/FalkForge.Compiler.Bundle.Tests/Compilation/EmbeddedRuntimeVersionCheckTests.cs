using System.Diagnostics;
using FalkForge.Compiler.Bundle.Compilation;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compilation;

/// <summary>
/// A compiler that signs a v3 envelope needs an engine and a companion that verify v3, or every
/// install fails at the customer with a signature error. The compiler learns each binary's version
/// from its Win32 version resource at build time. No resource means "unknown", which passes with a
/// warning, so the fake MZ files the rest of the suite embeds keep working. The warning reaches a log
/// only when the caller attached one; most callers do not.
/// </summary>
public sealed class EmbeddedRuntimeVersionCheckTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"RuntimeVer_{Guid.NewGuid():N}");

    public EmbeddedRuntimeVersionCheckTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose() => TestTemp.TryDelete(_tempDir);

    // A real PE with a version resource that is present on every machine running these tests.
    private static string VersionedBinary()
    {
        var path = typeof(object).Assembly.Location;
        if (string.IsNullOrEmpty(path) || FileVersionInfo.GetVersionInfo(path).ProductVersion is null or "")
            path = Environment.ProcessPath!;
        return path;
    }

    [Theory]
    [InlineData("engine")]
    [InlineData("elevation companion")]
    public void Check_BinaryOlderThanCompiler_ReturnsBDL038_NamingTheRole(string role)
    {
        var result = EmbeddedRuntimeVersionCheck.Check(VersionedBinary(), role, compilerInformationalVersion: "99.0.0");

        Assert.True(result.IsFailure);
        Assert.StartsWith("BDL038", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains(role, result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("99.0.0", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("scripts/publish.ps1", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_BinaryNewerThanCompiler_PassesWithoutWarning()
    {
        var result = EmbeddedRuntimeVersionCheck.Check(VersionedBinary(), "engine", compilerInformationalVersion: "0.0.1");

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Check_EqualVersions_PassesWithoutWarning()
    {
        // Equal is every correct build. A warning here would fire on all of them and be ignored.
        var binary = VersionedBinary();
        var own = FileVersionInfo.GetVersionInfo(binary).ProductVersion!;

        var result = EmbeddedRuntimeVersionCheck.Check(binary, "engine", compilerInformationalVersion: own);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Check_NoVersionResource_PassesWithWarning()
    {
        var fake = Path.Combine(_tempDir, "FalkForge.Engine.exe");
        File.WriteAllBytes(fake, [(byte)'M', (byte)'Z', 0xE1, 0xE7]);

        var result = EmbeddedRuntimeVersionCheck.Check(fake, "engine", compilerInformationalVersion: "99.0.0");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.StartsWith("BDL038", result.Value, StringComparison.Ordinal);
        Assert.Contains("no readable ProductVersion", result.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_UnreadableFile_PassesWithWarning()
    {
        var missing = Path.Combine(_tempDir, "not-there.exe");

        var result = EmbeddedRuntimeVersionCheck.Check(missing, "engine", compilerInformationalVersion: "99.0.0");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.StartsWith("BDL038", result.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_UnparseableCompilerVersion_PassesWithWarning()
    {
        var result = EmbeddedRuntimeVersionCheck.Check(VersionedBinary(), "engine", compilerInformationalVersion: "not-a-version");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Contains("not-a-version", result.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void CompilerVersion_IsSemVer()
    {
        Assert.True(ProductVersionOrder.TryParse(EmbeddedRuntimeVersionCheck.CompilerVersion, out _),
            $"Compiler informational version '{EmbeddedRuntimeVersionCheck.CompilerVersion}' is not SemVer 2");
    }
}
