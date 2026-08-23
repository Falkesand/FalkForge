namespace FalkForge.Engine.Tests.Pipeline;

using System.Text;
using FalkForge.Engine.Detection;
using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// PlanStep forwards the secret property VALUES collected through SetSecureProperty onto each planned
/// action so the executor can set them through a runtime transform. The secret must never land in the
/// command-line <see cref="PlanAction.Properties"/> dictionary.
/// </summary>
public sealed class PlanStepSecurePropertyTests
{
    private static InstallerManifest ManifestWith(PackageInfo package) =>
        new()
        {
            Name = "TestApp",
            Manufacturer = "Acme",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            Packages = [package]
        };

    private static PackageInfo MsiPackage(string id = "Pkg1") =>
        new()
        {
            Id = id,
            Type = PackageType.MsiPackage,
            DisplayName = $"Test {id}",
            SourcePath = $@"C:\fake\{id}.msi",
            Sha256Hash = "DEADBEEF"
        };

    private static PipelineContext CtxWith(InstallerManifest manifest) =>
        new()
        {
            Manifest = manifest,
            Detection = new DetectionResult(InstallState.NotInstalled, null, [])
        };

    [Fact]
    public async Task Plan_ForwardsSecurePropertyValuesToEachAction_AndKeepsThemOffProperties()
    {
        var ctx = CtxWith(ManifestWith(MsiPackage()));
        var channel = new FakeUiChannel();
        var step = new PlanStep(new Planner(), channel);

        var secure = new Dictionary<string, SensitiveBytes>(StringComparer.OrdinalIgnoreCase)
        {
            ["DB_PASSWORD"] = SensitiveBytes.FromPlaintext("p@ss w0rd!"u8)
        };
        var request = new UiRequest.Plan(
            InstallAction.Install,
            InstallDirectory: null,
            FeatureSelections: new Dictionary<string, bool>(),
            Properties: new Dictionary<string, string>(),
            SecureProperties: secure);

        var result = await step.ExecuteAsync(ctx, request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var action = Assert.Single(ctx.Plan!.Actions);

        Assert.True(action.SecureProperties.ContainsKey("DB_PASSWORD"));
        Assert.Equal(
            "p@ss w0rd!",
            Encoding.UTF8.GetString(action.SecureProperties["DB_PASSWORD"].Span));

        // The secret must never reach the command-line properties dictionary.
        Assert.False(action.Properties.ContainsKey("DB_PASSWORD"));
        Assert.DoesNotContain("[DB_PASSWORD]", action.Properties.Values);
    }
}
