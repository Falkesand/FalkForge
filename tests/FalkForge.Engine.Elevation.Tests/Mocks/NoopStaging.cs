namespace FalkForge.Engine.Elevation.Tests.Mocks;

using FalkForge.Engine.Elevation.Commands;

/// <summary>
/// A secret-transform staging that must never be reached. Used by tests whose install either carries no
/// secret block or is rejected before install, so a call here means the code touched the staging disk on a
/// path that should not — the throw surfaces that immediately.
/// </summary>
internal sealed class NoopStaging : ISecureTransformStaging
{
    public Result<SecureStagingLease> CreateStagingDirectory() =>
        throw new InvalidOperationException("Secret-transform staging must not be reached on this path.");
}
