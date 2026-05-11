using FalkForge.Builders;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Core.Tests.Builders;

/// <summary>
/// Files added via FeatureBuilder.Files() must survive the PackageBuilder.Feature() call
/// and appear in PackageModel.Files with the correct FeatureRef, so the compiler can
/// assign them to the right MSI Feature/Component graph.
/// </summary>
public sealed class FeatureBuilderFilesTests
{
    [Fact]
    public void Feature_AddFile_ScopedFileReachesPackageModel()
    {
        // WHY: feature-scoped files declared via the fluent API must reach PackageModel.Files
        // so the MSI compiler can include them in the correct feature's component group.
        // If this test fails it means the file is silently dropped during builder.Build().
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Acme";
            p.Feature("MainFeature", f =>
            {
                f.Files(fs => fs
                    .Add(@"C:\payload\app.exe")
                    .To(KnownFolder.ProgramFiles / "Acme" / "App"));
            });
        });

        Assert.NotEmpty(package.Files);
    }

    [Fact]
    public void Feature_AddFile_ScopedFileHasCorrectFeatureRef()
    {
        // WHY: the FeatureRef on each lifted file must match the feature ID so that
        // the MSI compiler assigns the component to the right feature row.
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Acme";
            p.Feature("MainFeature", f =>
            {
                f.Files(fs => fs
                    .Add(@"C:\payload\app.exe")
                    .To(KnownFolder.ProgramFiles / "Acme" / "App"));
            });
        });

        Assert.All(package.Files, file =>
            Assert.Equal("MainFeature", file.FeatureRef));
    }

    [Fact]
    public void Feature_AddFile_MultipleFeatures_FilesGetCorrectRefs()
    {
        // WHY: when two features each declare files, each file must carry its own feature's ref,
        // not bleed into the other feature or into the default "Complete" feature.
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Acme";
            p.Feature("FeatureA", f =>
            {
                f.Files(fs => fs
                    .Add(@"C:\payload\a.exe")
                    .To(KnownFolder.ProgramFiles / "Acme" / "App"));
            });
            p.Feature("FeatureB", f =>
            {
                f.Files(fs => fs
                    .Add(@"C:\payload\b.exe")
                    .To(KnownFolder.ProgramFiles / "Acme" / "App"));
            });
        });

        Assert.Equal(2, package.Files.Count);

        var fileA = Assert.Single(package.Files, f => f.FileName == "a.exe");
        Assert.Equal("FeatureA", fileA.FeatureRef);

        var fileB = Assert.Single(package.Files, f => f.FileName == "b.exe");
        Assert.Equal("FeatureB", fileB.FeatureRef);
    }
}
