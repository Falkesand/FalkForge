using FalkForge.Builders;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Core.Tests;

public sealed class ComRegistrationTests
{
    [Fact]
    public void ComClassBuilder_SetsAllProperties()
    {
        var classId = Guid.NewGuid();
        var appId = Guid.NewGuid();
        var model = new ComClassBuilder()
            .ClassId(classId)
            .LocalServer32()
            .ProgId("MyApp.Server")
            .Description("My COM Server")
            .ThreadingModel(ComThreadingModel.Both)
            .AppId(appId)
            .ComponentRef("comp1")
            .Build();

        Assert.Equal(classId, model.ClassId);
        Assert.Equal(ComServerType.LocalServer32, model.ServerType);
        Assert.Equal("MyApp.Server", model.ProgId);
        Assert.Equal("My COM Server", model.Description);
        Assert.Equal(ComThreadingModel.Both, model.ThreadingModel);
        Assert.Equal(appId, model.AppId);
        Assert.Equal("comp1", model.ComponentRef);
    }

    [Fact]
    public void ComClassBuilder_DefaultValues()
    {
        var classId = Guid.NewGuid();
        var model = new ComClassBuilder()
            .ClassId(classId)
            .Build();

        Assert.Equal(classId, model.ClassId);
        Assert.Equal(ComServerType.InprocServer32, model.ServerType);
        Assert.Equal(ComThreadingModel.Apartment, model.ThreadingModel);
        Assert.Null(model.ProgId);
        Assert.Null(model.Description);
        Assert.Null(model.AppId);
        Assert.Null(model.ComponentRef);
    }

    [Fact]
    public void ComTypeLibBuilder_SetsAllProperties()
    {
        var libId = Guid.NewGuid();
        var model = new ComTypeLibBuilder()
            .TypeLibId(libId)
            .Version(2, 1)
            .Language(1033)
            .Description("My Type Library")
            .ComponentRef("comp1")
            .Build();

        Assert.Equal(libId, model.TypeLibId);
        Assert.Equal(new Version(2, 1), model.Version);
        Assert.Equal(1033, model.Language);
        Assert.Equal("My Type Library", model.Description);
        Assert.Equal("comp1", model.ComponentRef);
    }

    [Fact]
    public void ComTypeLibBuilder_DefaultValues()
    {
        var libId = Guid.NewGuid();
        var model = new ComTypeLibBuilder()
            .TypeLibId(libId)
            .Build();

        Assert.Equal(libId, model.TypeLibId);
        Assert.Equal(new Version(1, 0), model.Version);
        Assert.Equal(0, model.Language);
        Assert.Null(model.Description);
        Assert.Null(model.ComponentRef);
    }

    [Fact]
    public void PackageBuilder_ComClass_AddsToModel()
    {
        var classId = Guid.NewGuid();
        var model = new PackageBuilder
            {
                Name = "Test",
                Manufacturer = "Test",
                Version = new Version(1, 0, 0),
                UpgradeCode = Guid.NewGuid()
            }
            .ComClass(c => c.ClassId(classId).ProgId("Test.Class"))
            .Build();

        Assert.Single(model.ComClasses);
        Assert.Equal(classId, model.ComClasses[0].ClassId);
        Assert.Equal("Test.Class", model.ComClasses[0].ProgId);
    }

    [Fact]
    public void PackageBuilder_TypeLib_AddsToModel()
    {
        var libId = Guid.NewGuid();
        var model = new PackageBuilder
            {
                Name = "Test",
                Manufacturer = "Test",
                Version = new Version(1, 0, 0),
                UpgradeCode = Guid.NewGuid()
            }
            .TypeLib(t => t.TypeLibId(libId).Version(3, 0).Language(1033))
            .Build();

        Assert.Single(model.TypeLibs);
        Assert.Equal(libId, model.TypeLibs[0].TypeLibId);
        Assert.Equal(new Version(3, 0), model.TypeLibs[0].Version);
        Assert.Equal(1033, model.TypeLibs[0].Language);
    }

    [Fact]
    public void PackageModel_ComCollections_DefaultToEmpty()
    {
        var model = new PackageBuilder
            {
                Name = "Test",
                Manufacturer = "Test",
                Version = new Version(1, 0, 0),
                UpgradeCode = Guid.NewGuid()
            }
            .Build();

        Assert.Empty(model.ComClasses);
        Assert.Empty(model.TypeLibs);
    }
}
