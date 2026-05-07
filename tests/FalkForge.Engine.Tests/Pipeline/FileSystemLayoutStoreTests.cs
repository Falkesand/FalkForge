namespace FalkForge.Engine.Tests.Pipeline;

using FalkForge.Engine.Pipeline;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Protocol;
using Xunit;

/// <summary>
/// Tests for FileSystemLayoutStore — the ILayoutStore adapter for manifest persistence.
/// RED: fails until FileSystemLayoutStore exists.
/// </summary>
public sealed class FileSystemLayoutStoreTests : IDisposable
{
    private readonly string _layoutPath;

    public FileSystemLayoutStoreTests()
    {
        _layoutPath = Path.Combine(Path.GetTempPath(), $"layout_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_layoutPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_layoutPath))
            Directory.Delete(_layoutPath, recursive: true);
    }

    private static InstallerManifest MakeManifest(string name = "TestApp") => new()
    {
        Name = name,
        Manufacturer = "Acme Corp",
        Version = "2.0.0",
        BundleId = Guid.NewGuid(),
        UpgradeCode = Guid.NewGuid(),
        Scope = InstallScope.PerMachine,
        Packages = []
    };

    // ──────────────────────────────────────────────────────────────────────────
    // Write + Read round-trip
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteAsync_Then_ReadAsync_Returns_Same_Manifest()
    {
        ILayoutStore store = new FileSystemLayoutStore();
        var manifest = MakeManifest("MyProduct");

        var writeResult = await store.WriteAsync(manifest, _layoutPath, CancellationToken.None);
        Assert.True(writeResult.IsSuccess);

        var readResult = await store.ReadAsync(_layoutPath, CancellationToken.None);
        Assert.True(readResult.IsSuccess);
        Assert.Equal("MyProduct", readResult.Value.Name);
        Assert.Equal("Acme Corp", readResult.Value.Manufacturer);
        Assert.Equal("2.0.0", readResult.Value.Version);
    }

    [Fact]
    public async Task WriteAsync_Creates_Manifest_Json_File()
    {
        ILayoutStore store = new FileSystemLayoutStore();
        var manifest = MakeManifest();

        await store.WriteAsync(manifest, _layoutPath, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_layoutPath, "manifest.json")));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ReadAsync on missing file
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadAsync_On_Empty_Directory_Returns_FileNotFound()
    {
        ILayoutStore store = new FileSystemLayoutStore();
        var emptyDir = Path.Combine(Path.GetTempPath(), $"empty_{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyDir);
        try
        {
            var result = await store.ReadAsync(emptyDir, CancellationToken.None);
            Assert.True(result.IsFailure);
            Assert.Equal(ErrorKind.FileNotFound, result.Error.Kind);
        }
        finally
        {
            Directory.Delete(emptyDir);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // WriteAsync on bad path
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteAsync_With_Empty_Path_Returns_Failure()
    {
        ILayoutStore store = new FileSystemLayoutStore();
        var result = await store.WriteAsync(MakeManifest(), string.Empty, CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.LayoutError, result.Error.Kind);
    }
}
