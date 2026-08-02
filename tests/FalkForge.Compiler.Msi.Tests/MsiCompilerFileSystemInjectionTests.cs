using System.Runtime.Versioning;
using FalkForge.Models;
using FalkForge.Platform;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

[SupportedOSPlatform("windows")]
public sealed class MsiCompilerFileSystemInjectionTests
{
    // Regression test for the "IFileSystem retained for public API compatibility" gap:
    // MsiCompiler(IFileSystem) advertised an injection seam that MsiAuthoring.Compile
    // silently ignored, always building its own WindowsFileSystem internally. A caller
    // supplying a virtual/test filesystem got real disk I/O with no indication anything
    // was wrong. This proves the constructor-supplied instance is the one actually
    // consulted during component resolution.
    [Fact]
    public void Compile_UsesConstructorInjectedFileSystem_ForComponentResolution()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiFsInjTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceFile = Path.Combine(tempDir, "app.exe");
            File.WriteAllText(sourceFile, "fake executable content for filesystem-injection test");

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "FsInjectionApp";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(1, 0, 0);
                p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "FsInjectionApp"));
            });

            var spyFileSystem = new SpyFileSystem();
            var compiler = new MsiCompiler(spyFileSystem);

            var result = compiler.Compile(package, outputDir);

            Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");
            Assert.Contains(sourceFile, spyFileSystem.FullPathCalls,
                StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            // Cleanup is best-effort; a locked handle or transient I/O error must not fail the test.
            TestTemp.TryDelete(tempDir);
        }
    }

    /// <summary>
    /// Wraps the real <see cref="WindowsFileSystem"/> so the compile pipeline still
    /// succeeds against real files on disk, while recording which paths were queried —
    /// proving this specific instance (not some other <see cref="IFileSystem"/>) is the
    /// one <c>ComponentResolver</c> actually calls.
    /// </summary>
    private sealed class SpyFileSystem : IFileSystem
    {
        private readonly WindowsFileSystem _inner = new();

        public List<string> FullPathCalls { get; } = [];

        public bool FileExists(string path) => _inner.FileExists(path);
        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
        public IReadOnlyList<string> GetFiles(string directory, string pattern, bool recursive) => _inner.GetFiles(directory, pattern, recursive);
        public IReadOnlyList<string> GetDirectories(string directory) => _inner.GetDirectories(directory);
        public long GetFileSize(string path) => _inner.GetFileSize(path);
        public byte[] ReadAllBytes(string path) => _inner.ReadAllBytes(path);
        public Stream OpenRead(string path) => _inner.OpenRead(path);
        public string GetRelativePath(string relativeTo, string path) => _inner.GetRelativePath(relativeTo, path);

        public string GetFullPath(string path)
        {
            FullPathCalls.Add(path);
            return _inner.GetFullPath(path);
        }

        public string GetFileName(string path) => _inner.GetFileName(path);
        public string GetDirectoryName(string path) => _inner.GetDirectoryName(path);
        public string GetFileHash(string path) => _inner.GetFileHash(path);
        public DateTime GetLastWriteTimeUtc(string path) => _inner.GetLastWriteTimeUtc(path);
    }
}
