namespace FalkForge.Engine.Tests.Mocks;

using FalkForge.Platform;
using FalkForge.Testing;

public sealed class MockPlatformServices : IPlatformServices
{
    public MockPlatformServices(
        IFileSystem? fileSystem = null,
        IRegistry? registry = null,
        IEnvironment? environment = null)
    {
        FileSystem = fileSystem ?? new MockFileSystem();
        Registry = registry ?? new MockRegistry();
        Environment = environment ?? new MockEnvironment();
    }

    public IFileSystem FileSystem { get; }
    public IRegistry Registry { get; }
    public IEnvironment Environment { get; }
}
