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

    [Fact]
    public void CheckVersion_BinaryIsSdkDefault_ReturnsBDL038EvenThoughItSortsNewer()
    {
        // 1.0.0 sorts above 0.5.0-beta.9, so the plain newer-than-compiler compare would pass this.
        // It is the SDK's default when a project sets no <Version>, not a real release.
        var result = EmbeddedRuntimeVersionCheck.CheckVersion("1.0.0", "0.5.0-beta.9", "C:\\bin\\FalkForge.Engine.exe", "engine");

        Assert.True(result.IsFailure);
        Assert.StartsWith("BDL038", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("SDK's default", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("FalkForge.Engine.Sources", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("0.5.0-beta.9", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("C:\\bin\\FalkForge.Engine.exe", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckVersion_BinaryIsSdkDefaultWithBuildMetadata_ReturnsBDL038()
    {
        // Build metadata is stripped before comparison, so 1.0.0+abc is still exactly 1.0.0.
        var result = EmbeddedRuntimeVersionCheck.CheckVersion("1.0.0+abc", "0.5.0-beta.9", "C:\\bin\\FalkForge.Engine.exe", "engine");

        Assert.True(result.IsFailure);
        Assert.StartsWith("BDL038", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("SDK's default", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckVersion_NormalOlderVersion_StillReturnsTheOlderVersionMessage()
    {
        // A genuinely older release must keep the original wording, not the 1.0.0 wording.
        var result = EmbeddedRuntimeVersionCheck.CheckVersion("0.4.0", "0.5.0-beta.9", "C:\\bin\\FalkForge.Engine.exe", "engine");

        Assert.True(result.IsFailure);
        Assert.StartsWith("BDL038", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("older than this", result.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SDK's default", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CompilerVersion_IsStillBelow_1_0_0()
    {
        // The 1.0.0-is-the-SDK-default rule in CheckVersion only makes sense while the compiler itself
        // has never shipped 1.0.0. Once it does, "1.0.0" is a real compiler version too, and the rule in
        // EmbeddedRuntimeVersionCheck.CheckVersion needs to be revisited before release.
        var isBelowOneZero = ProductVersionOrder.Compare(EmbeddedRuntimeVersionCheck.CompilerVersion, "1.0.0") < 0;

        Assert.True(isBelowOneZero,
            $"Compiler version '{EmbeddedRuntimeVersionCheck.CompilerVersion}' is at or past 1.0.0. " +
            "The BDL038 rule that treats a binary's 1.0.0 ProductVersion as the SDK default (not a real " +
            "release) must be revisited before release: 1.0.0 is now a version the compiler itself can carry.");
    }
}
