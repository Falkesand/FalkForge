using System.Runtime.Versioning;
using FalkForge.Extensibility;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe;

/// <summary>
/// A4: proves <see cref="IComponentContributor.GetAdditionalFiles"/> output reaches the compiled
/// MSI's File table (with a Component + FeatureComponent row, same as a regular package file)
/// instead of being silently dropped behind the (now-retired) EXT002 warning. See
/// <see cref="FalkForge.Compiler.Msi.Tests.LoggingInstrumentationTests"/> for the
/// warning-retirement coverage. Mirrors <c>RealExtensionEmissionTests</c>' compile-then-query
/// pattern.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ComponentContributorEmissionTests
{
    [Fact]
    public void ComponentContributor_AdditionalFile_ReachesCompiledMsiFileTable()
    {
        using var scratch = new Scratch();

        var sourceFile = Path.Combine(scratch.SourceDir, "app.exe");
        File.WriteAllText(sourceFile, "primary payload");

        var extraFile = Path.Combine(scratch.SourceDir, "extra.dll");
        File.WriteAllText(extraFile, "payload contributed by extension");

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "ComponentContribEmitApp";
            p.Manufacturer = "Corp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "Corp" / "ComponentContribEmitApp"));
        });

        var compiler = new MsiCompiler(new WindowsFileSystem())
            .Use(new FileContributingExtension(extraFile));

        var result = compiler.Compile(package, scratch.OutputDir);
        Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");

        using var db = MsiDatabase.Open(result.Value, readOnly: true).Value;

        var fileRows = db.QueryRows("SELECT `FileName` FROM `File`", 1);
        Assert.True(fileRows.IsSuccess, fileRows.IsFailure ? fileRows.Error.Message : "");
        Assert.Contains(fileRows.Value, r => r[0] == "extra.dll");

        // A component (with a FeatureComponent row) must exist too, not just a bare File row —
        // otherwise the file would be orphaned and Windows Installer would never install it.
        var featureComponentRows = db.QueryRows("SELECT `Feature_`, `Component_` FROM `FeatureComponents`", 2);
        Assert.True(featureComponentRows.IsSuccess, featureComponentRows.IsFailure ? featureComponentRows.Error.Message : "");
        Assert.True(featureComponentRows.Value.Count >= 2, "Expected components for both the primary and contributed files.");
    }

    private sealed class FileContributingExtension(string extraFilePath) : IFalkForgeExtension, IComponentContributor
    {
        public string Name => "TestFileContributor";

        public void Register(IExtensionRegistry registry) => registry.RegisterComponentContributor(this);

        public IReadOnlyList<FileEntryModel> GetAdditionalFiles(ExtensionContext context) =>
        [
            new FileEntryModel
            {
                SourcePath = extraFilePath,
                TargetDirectory = KnownFolder.ProgramFiles / context.Package.Manufacturer / context.Package.Name,
                FileName = "extra.dll",
            },
        ];
    }

    private sealed class Scratch : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"CompContribEmit_{Guid.NewGuid():N}");

        public Scratch()
        {
            SourceDir = Path.Combine(_root, "source");
            OutputDir = Path.Combine(_root, "output");
            Directory.CreateDirectory(SourceDir);
            Directory.CreateDirectory(OutputDir);
        }

        public string SourceDir { get; }
        public string OutputDir { get; }

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }
}
