using FalkForge.Platform;

namespace FalkForge.Testing;

/// <summary>
/// Deterministic <see cref="IPlatformServices"/> double for tests that drive production code
/// which reads machine state (e.g. <c>FalkForge.Engine.Variables.BuiltInVariables.Populate</c>)
/// through the platform seam instead of calling the OS directly. Wraps the caller's own
/// <see cref="IRegistry"/> (typically a <see cref="MockRegistry"/>) so registry-dependent built-ins
/// (e.g. <c>RebootPending</c>) are deterministic in tests without needing a real Windows registry.
/// <c>Privileged</c> is no longer registry-dependent — it reads <see cref="IEnvironment.IsElevated"/>
/// (deterministic via <see cref="FakeEnvironment"/>) combined with whether an elevation companion
/// is configured for the session, not a registry key.
/// </summary>
public sealed class FakePlatformServices : IPlatformServices
{
    public FakePlatformServices(IRegistry registry, IEnvironment? environment = null, IFileSystem? fileSystem = null)
    {
        Registry = registry;
        Environment = environment ?? new FakeEnvironment();
        FileSystem = fileSystem ?? new MockFileSystem();
    }

    public IFileSystem FileSystem { get; }
    public IRegistry Registry { get; }
    public IEnvironment Environment { get; }
}
