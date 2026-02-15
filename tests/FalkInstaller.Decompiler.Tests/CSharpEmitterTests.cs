using FalkInstaller.Models;
using Xunit;

namespace FalkInstaller.Decompiler.Tests;

public sealed class CSharpEmitterTests
{
    [Fact]
    public void Emit_SimplePackage_ContainsBasicMetadata()
    {
        var model = CreateMinimalPackage();
        var emitter = new CSharpEmitter();

        var source = emitter.Emit(model);

        Assert.Contains("builder.Name = \"Test App\"", source);
        Assert.Contains("builder.Manufacturer = \"Test Corp\"", source);
        Assert.Contains("new Version(1, 0, 0)", source);
    }

    [Fact]
    public void Emit_WithDescription_IncludesDescription()
    {
        var model = CreateMinimalPackage(description: "A test application");
        var emitter = new CSharpEmitter();

        var source = emitter.Emit(model);

        Assert.Contains("builder.Description = \"A test application\"", source);
    }

    [Fact]
    public void Emit_WithFeatures_GeneratesFeatureCode()
    {
        var model = new PackageModel
        {
            Name = "Test App",
            Manufacturer = "Test Corp",
            Version = new Version(1, 0, 0),
            Features =
            [
                new FeatureModel
                {
                    Id = "Main",
                    Title = "Main Feature",
                    Description = "The main feature",
                    IsRequired = true
                }
            ]
        };

        var emitter = new CSharpEmitter();
        var source = emitter.Emit(model);

        Assert.Contains("builder.Feature(\"Main\"", source);
        Assert.Contains("f.Title(\"Main Feature\")", source);
        Assert.Contains("f.Description(\"The main feature\")", source);
        Assert.Contains("f.Required()", source);
    }

    [Fact]
    public void Emit_WithRegistryEntries_GeneratesRegistryCode()
    {
        var model = new PackageModel
        {
            Name = "Test App",
            Manufacturer = "Test Corp",
            Version = new Version(1, 0, 0),
            RegistryEntries =
            [
                new RegistryEntryModel
                {
                    Root = RegistryRoot.LocalMachine,
                    Key = "SOFTWARE\\Test",
                    ValueName = "Version",
                    Value = "1.0"
                }
            ]
        };

        var emitter = new CSharpEmitter();
        var source = emitter.Emit(model);

        Assert.Contains("builder.Registry(r =>", source);
        Assert.Contains("RegistryRoot.LocalMachine", source);
        Assert.Contains("SOFTWARE\\\\Test", source);
    }

    [Fact]
    public void Emit_WithServices_GeneratesServiceCode()
    {
        var model = new PackageModel
        {
            Name = "Test App",
            Manufacturer = "Test Corp",
            Version = new Version(1, 0, 0),
            Services =
            [
                new ServiceModel
                {
                    Name = "MySvc",
                    DisplayName = "My Service",
                    Executable = "svc.exe",
                    Description = "A service"
                }
            ]
        };

        var emitter = new CSharpEmitter();
        var source = emitter.Emit(model);

        Assert.Contains("builder.Service(\"MySvc\"", source);
        Assert.Contains("s.DisplayName(\"My Service\")", source);
        Assert.Contains("s.Executable(\"svc.exe\")", source);
        Assert.Contains("s.Description(\"A service\")", source);
    }

    [Fact]
    public void Emit_WithShortcuts_GeneratesShortcutCode()
    {
        var model = new PackageModel
        {
            Name = "Test App",
            Manufacturer = "Test Corp",
            Version = new Version(1, 0, 0),
            Shortcuts =
            [
                new ShortcutModel
                {
                    Name = "My App",
                    TargetFile = "app.exe",
                    Locations = [ShortcutLocation.Desktop],
                    Description = "Launch the app"
                }
            ]
        };

        var emitter = new CSharpEmitter();
        var source = emitter.Emit(model);

        Assert.Contains("builder.Shortcut(\"My App\", \"app.exe\")", source);
        Assert.Contains("ShortcutLocation.Desktop", source);
        Assert.Contains(".Add()", source);
    }

    [Fact]
    public void Emit_WithProperties_GeneratesPropertyCode()
    {
        var model = new PackageModel
        {
            Name = "Test App",
            Manufacturer = "Test Corp",
            Version = new Version(1, 0, 0),
            Properties =
            [
                new PropertyModel { Name = "MY_PROP", Value = "my_value" }
            ]
        };

        var emitter = new CSharpEmitter();
        var source = emitter.Emit(model);

        Assert.Contains("builder.Property(\"MY_PROP\", \"my_value\")", source);
    }

    [Fact]
    public void Emit_IncludesUsingStatements()
    {
        var model = CreateMinimalPackage();
        var emitter = new CSharpEmitter();

        var source = emitter.Emit(model);

        Assert.Contains("using FalkInstaller;", source);
        Assert.Contains("using FalkInstaller.Builders;", source);
        Assert.Contains("using FalkInstaller.Models;", source);
    }

    [Fact]
    public void Emit_EndsWith_BuildCall()
    {
        var model = CreateMinimalPackage();
        var emitter = new CSharpEmitter();

        var source = emitter.Emit(model);

        Assert.Contains("var model = builder.Build();", source);
    }

    [Fact]
    public void Emit_EscapesSpecialCharacters()
    {
        var model = new PackageModel
        {
            Name = "Test \"App\"",
            Manufacturer = "Test\\Corp",
            Version = new Version(1, 0, 0)
        };

        var emitter = new CSharpEmitter();
        var source = emitter.Emit(model);

        Assert.Contains("Test \\\"App\\\"", source);
        Assert.Contains("Test\\\\Corp", source);
    }

    private static PackageModel CreateMinimalPackage(string? description = null)
    {
        return new PackageModel
        {
            Name = "Test App",
            Manufacturer = "Test Corp",
            Version = new Version(1, 0, 0),
            Description = description
        };
    }
}
