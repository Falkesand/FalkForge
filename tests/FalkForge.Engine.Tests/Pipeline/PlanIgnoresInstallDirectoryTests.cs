namespace FalkForge.Engine.Tests.Pipeline;

using FalkForge.Engine.Detection;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// Pins a gap rather than a behaviour: the installation directory a UI collects never reaches the
/// plan. <see cref="UiRequest.Plan"/> carries it, the channel accumulates it from
/// <c>SetInstallDirectoryMessage</c>, and <see cref="PlanStep"/> reads Action, LicenseAccepted,
/// Properties, FeatureSelections, PackageFeatureSelections and SecureProperties, and nothing else.
/// Every package installs where its own MSI puts it.
/// <para>
/// Closing that gap is not a wiring job. A bundle installs several packages and the manifest says
/// nothing about which of them the user's directory applies to, nor which property each package
/// reads it from: FalkForge's own MSIs use <c>INSTALLDIR</c>, a third-party MSI in the same chain
/// may use a different name or none, and a public property has to be listed in
/// <c>SecureCustomProperties</c> to survive an elevated install. Stamping one name onto every
/// action would redirect prerequisites into the application's folder.
/// </para>
/// <para>
/// So the built-in wizard keeps its directory page out of the walk
/// (<c>InstallDirPageIsOutOfTheWalkTests</c> in FalkForge.Ui.Tests). When the directory does reach
/// the plan, this test fails, and that is the signal to put the page back into the walk.
/// </para>
/// </summary>
public sealed class PlanIgnoresInstallDirectoryTests
{
    private const string RequestedDirectory = @"C:\Chosen\By\The\User";

    private static InstallerManifest ManifestWithTwoPackages() =>
        new()
        {
            Name = "TestApp",
            Manufacturer = "Acme",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            Packages =
            [
                MsiPackage("Prereq"),
                MsiPackage("App")
            ]
        };

    private static PackageInfo MsiPackage(string id) =>
        new()
        {
            Id = id,
            Type = PackageType.MsiPackage,
            DisplayName = $"Test {id}",
            SourcePath = $@"C:\fake\{id}.msi",
            Sha256Hash = "DEADBEEF"
        };

    [Fact]
    public async Task Plan_WithAnInstallDirectory_PutsItOnNoAction()
    {
        var ctx = new PipelineContext
        {
            Manifest = ManifestWithTwoPackages(),
            Detection = new DetectionResult(InstallState.NotInstalled, null, [])
        };
        await using var channel = new FakeUiChannel();
        var step = new PlanStep(new Planner(), channel);

        var request = new UiRequest.Plan(
            InstallAction.Install,
            InstallDirectory: RequestedDirectory,
            FeatureSelections: new Dictionary<string, bool>(),
            Properties: new Dictionary<string, string>(),
            SecureProperties: new Dictionary<string, SensitiveBytes>());

        var result = await step.ExecuteAsync(ctx, request, CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.NotNull(ctx.Plan);
        Assert.NotEmpty(ctx.Plan.Actions);

        foreach (var action in ctx.Plan.Actions)
            Assert.DoesNotContain(action.Properties, p => p.Value == RequestedDirectory);
    }
}
