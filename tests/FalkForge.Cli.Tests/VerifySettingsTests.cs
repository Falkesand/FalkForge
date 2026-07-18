using FalkForge.Cli.Settings;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Validation tests for <see cref="VerifySettings"/>. Two independent verification modes exist:
/// <c>--rebuild &lt;project&gt;</c> (rebuild-and-byte-compare, both .msi and .exe) and, for .msi
/// only, a signature-only mode that checks the embedded/detached integrity signature without a
/// source project. A bundle (.exe) has no signature-only mode yet, so <c>--rebuild</c> stays
/// required for it.
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
    public void Validate_MsiWithoutRebuild_Succeeds_SignatureOnlyMode()
    {
        // .msi has a second verification mode (the embedded/detached ECDSA signature), so
        // omitting --rebuild is valid — it selects that mode instead of rejecting the invocation.
        var settings = Make("app.msi", null);

        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Validate_ExeWithoutRebuild_Fails()
    {
        // Bundles have no signature-only verification mode (yet) — --rebuild is the only way to
        // verify a .exe, so omitting it must still be rejected rather than silently no-op.
        var settings = Make("installer.exe", null);

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Validate_UnsupportedArtifactExtension_Fails()
    {
        var settings = Make("app.zip", "proj.csproj");

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Validate_TrustedKeyWithWhitespace_Fails()
    {
        var settings = new VerifySettings { ArtifactPath = "app.msi", TrustedKeys = ["  "] };

        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void Validate_TrustedKeyWithRebuild_Fails()
    {
        // Merge Gate nit: --trusted-key only means anything in signature-only mode (no --rebuild).
        // Combined with --rebuild it was silently ignored — the rebuild-and-compare path never
        // reads TrustedKeys at all — which is a fail-loud violation: a user who passes --trusted-key
        // expecting it to matter gets no error and no effect. Reject the combination instead.
        var settings = new VerifySettings
        {
            ArtifactPath = "app.msi",
            RebuildProjectPath = "proj.csproj",
            TrustedKeys = ["AABBCC"]
        };

        Assert.False(settings.Validate().Successful);
    }
}
