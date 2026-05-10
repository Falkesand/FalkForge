namespace FalkForge.Engine.Tests.Layout;

using System.Net;
using System.Security.Cryptography;
using FalkForge.Engine.Download;
using FalkForge.Engine.Layout;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Logging;
using Xunit;

[Collection(EngineMeterCollection.Name)]
public sealed class LayoutManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _layoutDir;
    private readonly string _sourceDir;

    public LayoutManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FalkLayoutTest_{Guid.NewGuid():N}");
        _layoutDir = Path.Combine(_tempDir, "layout");
        _sourceDir = Path.Combine(_tempDir, "source");
        Directory.CreateDirectory(_sourceDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (IOException) { /* best effort cleanup */ }
    }

    [Fact]
    public async Task CreateLayoutAsync_CreatesLayoutDirectory()
    {
        var manager = CreateManager();
        var manifest = CreateManifest();

        var result = await manager.CreateLayoutAsync(manifest, _layoutDir, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(Directory.Exists(_layoutDir));
    }

    [Fact]
    public async Task CreateLayoutAsync_CopiesEmbeddedPayloads()
    {
        var content = "MSI payload content"u8.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(content));
        var sourcePath = Path.Combine(_sourceDir, "app.msi");
        File.WriteAllBytes(sourcePath, content);

        var manifest = CreateManifest(CreatePackage("App", sourcePath, hash));
        var manager = CreateManager();

        var result = await manager.CreateLayoutAsync(manifest, _layoutDir, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var layoutFile = Path.Combine(_layoutDir, "app.msi");
        Assert.True(File.Exists(layoutFile));
        Assert.Equal(content, File.ReadAllBytes(layoutFile));
    }

    [Fact]
    public async Task CreateLayoutAsync_GeneratesManifestFile()
    {
        var content = "manifest test"u8.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(content));
        var sourcePath = Path.Combine(_sourceDir, "app.msi");
        File.WriteAllBytes(sourcePath, content);

        var manifest = CreateManifest(CreatePackage("App", sourcePath, hash));
        var manager = CreateManager();

        var result = await manager.CreateLayoutAsync(manifest, _layoutDir, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var manifestPath = Path.Combine(_layoutDir, "manifest.json");
        Assert.True(File.Exists(manifestPath));

        var manifestJson = File.ReadAllText(manifestPath);
        Assert.Contains("TestApp", manifestJson);
        Assert.Contains("TestCo", manifestJson);
    }

    [Fact]
    public async Task CreateLayoutAsync_HashVerificationOnCopiedFiles()
    {
        var content = "hash verification test"u8.ToArray();
        var wrongHash = "0000000000000000000000000000000000000000000000000000000000000000";
        var sourcePath = Path.Combine(_sourceDir, "bad.msi");
        File.WriteAllBytes(sourcePath, content);

        var manifest = CreateManifest(CreatePackage("Bad", sourcePath, wrongHash));
        var manager = CreateManager();

        var result = await manager.CreateLayoutAsync(manifest, _layoutDir, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.LayoutError, result.Error.Kind);
        Assert.Contains("SHA-256 hash mismatch", result.Error.Message);
    }

    [Fact]
    public async Task CreateLayoutAsync_EmptyManifest_CreatesEmptyLayout()
    {
        var manifest = CreateManifest();
        var manager = CreateManager();

        var result = await manager.CreateLayoutAsync(manifest, _layoutDir, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(Directory.Exists(_layoutDir));
        // Only manifest.json should exist
        var files = Directory.GetFiles(_layoutDir);
        Assert.Single(files);
        Assert.Equal("manifest.json", Path.GetFileName(files[0]));
    }

    [Fact]
    public async Task CreateLayoutAsync_RemotePayload_DownloadsToLayout()
    {
        var content = "remote payload"u8.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(content));
        var package = new PackageInfo
        {
            Id = "Remote",
            Type = PackageType.MsiPackage,
            DisplayName = "Remote Package",
            SourcePath = "remote.msi",
            Sha256Hash = hash,
            DownloadUrl = "https://example.com/remote.msi"
        };

        var manifest = CreateManifest(package);
        var handler = new MockHttpHandler(content, HttpStatusCode.OK);
        using var client = new HttpClient(handler);
        var downloader = new PayloadDownloader(client);
        var manager = new LayoutManager(downloader);

        var result = await manager.CreateLayoutAsync(manifest, _layoutDir, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var layoutFile = Path.Combine(_layoutDir, "remote.msi");
        Assert.True(File.Exists(layoutFile));
        Assert.Equal(content, File.ReadAllBytes(layoutFile));
    }

    [Fact]
    public async Task CreateLayoutAsync_MissingSourceNoUrl_ReturnsFailure()
    {
        var package = CreatePackage("Missing", @"C:\nonexistent\app.msi", "AABB");
        var manifest = CreateManifest(package);
        var manager = CreateManager();

        var result = await manager.CreateLayoutAsync(manifest, _layoutDir, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.LayoutError, result.Error.Kind);
        Assert.Contains("not found", result.Error.Message);
    }

    private static LayoutManager CreateManager()
    {
        using var client = new HttpClient();
        var downloader = new PayloadDownloader(client);
        return new LayoutManager(downloader);
    }

    private static InstallerManifest CreateManifest(params PackageInfo[] packages)
    {
        return new InstallerManifest
        {
            Name = "TestApp",
            Manufacturer = "TestCo",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerUser,
            Packages = packages
        };
    }

    private static PackageInfo CreatePackage(string id, string sourcePath, string sha256Hash)
    {
        return new PackageInfo
        {
            Id = id,
            Type = PackageType.MsiPackage,
            DisplayName = $"Test {id}",
            SourcePath = sourcePath,
            Sha256Hash = sha256Hash
        };
    }

    private sealed class MockHttpHandler : HttpMessageHandler
    {
        private readonly byte[] _content;
        private readonly HttpStatusCode _statusCode;

        public MockHttpHandler(byte[] content, HttpStatusCode statusCode)
        {
            _content = content;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new ByteArrayContent(_content)
            });
        }
    }
}
