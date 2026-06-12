using FalkForge.Cli.Settings;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Validation tests for <see cref="VerifySettings"/>. The settings gate ensures the user
/// supplied both an artifact path and a <c>--rebuild</c> project before any subprocess runs,
/// and that the artifact extension is one the verifier can byte-compare (.msi or .exe).
/// </summary>
public sealed class VerifySettingsTests
{
    private static VerifySettings Make(string artifact, string? rebuild) =>
        new() { ArtifactPath = artifact, RebuildProjectPath = rebuild ?? string.Empty };

    [Fact]
    public void Validate_MsiArtifactWithRebuild_Succeeds()
    {
        var settings = Make("app.msi", "proj.csproj");

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Validate_ExeArtifactWithRebuild_Succeeds()
    {
        var settings = Make("installer.exe", "proj.csproj");

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Validate_MissingArtifact_Fails()
    {
        var settings = Make("", "proj.csproj");

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Validate_MissingRebuildProject_Fails()
    {
        // --rebuild is the only supported verification mode today; without it there is
        // nothing to compare against, so the command must reject the invocation.
        var settings = Make("app.msi", null);

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Validate_UnsupportedArtifactExtension_Fails()
    {
        var settings = Make("app.zip", "proj.csproj");

        Assert.False(settings.Validate().Successful);
    }
}
