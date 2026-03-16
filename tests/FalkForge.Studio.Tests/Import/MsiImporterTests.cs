using FalkForge;
using FalkForge.Models;
using FalkForge.Studio.Import;
using Xunit;

namespace FalkForge.Studio.Tests.Import;

public sealed class MsiImporterTests
{
    [Fact]
    public void FromPackageModel_MapsProductSection()
    {
        var model = new PackageModel
        {
            Name = "Test App",
            Manufacturer = "Acme Corp",
            Version = new Version(2, 5, 1),
            UpgradeCode = Guid.Parse("12345678-1234-1234-1234-123456789012"),
            Scope = InstallScope.PerUser,
            Architecture = ProcessorArchitecture.X64,
            Description = "A test application",
            Comments = "Some comments",
            HelpUrl = "https://help.example.com",
            AboutUrl = "https://about.example.com",
            UpdateUrl = "https://update.example.com",
            LicenseFile = "license.rtf"
        };

        var result = MsiImporter.FromPackageModel(model);

        Assert.True(result.IsSuccess);
        var product = result.Value.Product;
        Assert.Equal("Test App", product.Name);
        Assert.Equal("Acme Corp", product.Manufacturer);
        Assert.Equal("2.5.1", product.Version);
        Assert.Equal("12345678-1234-1234-1234-123456789012", product.UpgradeCode);
        Assert.Equal("perUser", product.Scope);
        Assert.Equal("x64", product.Architecture);
        Assert.Equal("A test application", product.Description);
        Assert.Equal("Some comments", product.Comments);
        Assert.Equal("https://help.example.com", product.HelpUrl);
        Assert.Equal("https://about.example.com", product.AboutUrl);
        Assert.Equal("https://update.example.com", product.UpdateUrl);
        Assert.Equal("license.rtf", product.LicenseFile);
    }

    [Fact]
    public void FromPackageModel_MapsPerMachineScope()
    {
        var model = CreateMinimalModel(scope: InstallScope.PerMachine);

        var result = MsiImporter.FromPackageModel(model);

        Assert.True(result.IsSuccess);
        Assert.Equal("perMachine", result.Value.Product.Scope);
    }

    [Fact]
    public void FromPackageModel_EmptyUpgradeCode_MapsToNull()
    {
        var model = CreateMinimalModel();

        var result = MsiImporter.FromPackageModel(model);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Product.UpgradeCode);
    }

    [Fact]
    public void FromPackageModel_MapsFeatures()
    {
        var model = new PackageModel
        {
            Name = "Test",
            Manufacturer = "Test",
            Version = new Version(1, 0, 0),
            Features = new List<FeatureModel>
            {
                new()
                {
                    Id = "Main",
                    Title = "Main Feature",
                    Description = "The main feature",
                    IsDefault = true,
                    IsRequired = true,
                    DisplayLevel = 1,
                    ComponentRefs = []
                },
                new()
                {
                    Id = "Optional",
                    Title = "Optional Feature",
                    IsDefault = false,
                    IsRequired = false,
                    DisplayLevel = 2,
                    ComponentRefs = []
                }
            }
        };

        var result = MsiImporter.FromPackageModel(model);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Features.Count);

        var main = result.Value.Features[0];
        Assert.Equal("Main", main.Id);
        Assert.Equal("Main Feature", main.Title);
        Assert.Equal("The main feature", main.Description);
        Assert.True(main.IsDefault);
        Assert.True(main.IsRequired);
        Assert.Equal(1, main.InstallLevel);

        var optional = result.Value.Features[1];
        Assert.Equal("Optional", optional.Id);
        Assert.False(optional.IsDefault);
        Assert.False(optional.IsRequired);
        Assert.Equal(2, optional.InstallLevel);
    }

    [Fact]
    public void FromPackageModel_MapsNestedFeatures()
    {
        var model = new PackageModel
        {
            Name = "Test",
            Manufacturer = "Test",
            Version = new Version(1, 0, 0),
            Features = new List<FeatureModel>
            {
                new()
                {
                    Id = "Parent",
                    Title = "Parent",
                    ComponentRefs = [],
                    Children = new List<FeatureModel>
                    {
                        new()
                        {
                            Id = "Child",
                            Title = "Child Feature",
                            ComponentRefs = []
                        }
                    }
                }
            }
        };

        var result = MsiImporter.FromPackageModel(model);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Features);
        Assert.NotNull(result.Value.Features[0].Features);
        Assert.Single(result.Value.Features[0].Features!);
        Assert.Equal("Child", result.Value.Features[0].Features![0].Id);
    }

    [Fact]
    public void FromPackageModel_MapsFilesToFeaturesByComponentRef()
    {
        var installPath = KnownFolder.ProgramFiles / "TestApp";

        var model = new PackageModel
        {
            Name = "Test",
            Manufacturer = "Test",
            Version = new Version(1, 0, 0),
            Files = new List<FileEntryModel>
            {
                new()
                {
                    SourcePath = "app.exe",
                    TargetDirectory = installPath,
                    FileName = "app.exe",
                    ComponentId = "comp_app",
                    Vital = true
                },
                new()
                {
                    SourcePath = "readme.txt",
                    TargetDirectory = installPath,
                    FileName = "readme.txt",
                    ComponentId = "comp_docs",
                    Vital = false
                }
            },
            Features = new List<FeatureModel>
            {
                new()
                {
                    Id = "Main",
                    Title = "Main",
                    ComponentRefs = ["comp_app"]
                },
                new()
                {
                    Id = "Docs",
                    Title = "Documentation",
                    ComponentRefs = ["comp_docs"]
                }
            }
        };

        var result = MsiImporter.FromPackageModel(model);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Features[0].Files);
        Assert.Equal("app.exe", result.Value.Features[0].Files[0].Source);
        Assert.True(result.Value.Features[0].Files[0].Vital);

        Assert.Single(result.Value.Features[1].Files);
        Assert.Equal("readme.txt", result.Value.Features[1].Files[0].Source);
        Assert.False(result.Value.Features[1].Files[0].Vital);
    }

    [Fact]
    public void FromPackageModel_MapsRegistry()
    {
        var model = new PackageModel
        {
            Name = "Test",
            Manufacturer = "Test",
            Version = new Version(1, 0, 0),
            RegistryEntries = new List<RegistryEntryModel>
            {
                new()
                {
                    Root = RegistryRoot.LocalMachine,
                    Key = @"SOFTWARE\TestApp",
                    ValueName = "InstallDir",
                    Value = @"C:\Program Files\TestApp",
                    ValueType = RegistryValueType.String
                },
                new()
                {
                    Root = RegistryRoot.CurrentUser,
                    Key = @"SOFTWARE\TestApp",
                    ValueName = "Version",
                    Value = 42,
                    ValueType = RegistryValueType.DWord
                }
            }
        };

        var result = MsiImporter.FromPackageModel(model);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Registry.Count);

        var reg0 = result.Value.Registry[0];
        Assert.Equal("LocalMachine", reg0.Root);
        Assert.Equal(@"SOFTWARE\TestApp", reg0.Key);
        Assert.Equal("InstallDir", reg0.ValueName);
        Assert.Equal(@"C:\Program Files\TestApp", reg0.Value);
        Assert.Equal("String", reg0.ValueType);

        var reg1 = result.Value.Registry[1];
        Assert.Equal("CurrentUser", reg1.Root);
        Assert.Equal("DWord", reg1.ValueType);
        Assert.Equal("42", reg1.Value);
    }

    [Fact]
    public void FromPackageModel_MapsServices()
    {
        var model = new PackageModel
        {
            Name = "Test",
            Manufacturer = "Test",
            Version = new Version(1, 0, 0),
            Services = new List<ServiceModel>
            {
                new()
                {
                    Name = "TestSvc",
                    DisplayName = "Test Service",
                    Executable = "svc.exe",
                    Description = "A test service",
                    StartMode = ServiceStartMode.Automatic,
                    Account = ServiceAccount.LocalSystem
                }
            }
        };

        var result = MsiImporter.FromPackageModel(model);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Services);
        var svc = result.Value.Services[0];
        Assert.Equal("TestSvc", svc.Name);
        Assert.Equal("Test Service", svc.DisplayName);
        Assert.Equal("svc.exe", svc.Executable);
        Assert.Equal("A test service", svc.Description);
        Assert.Equal("Automatic", svc.StartMode);
        Assert.Equal("LocalSystem", svc.Account);
    }

    [Fact]
    public void FromPackageModel_MapsShortcuts()
    {
        var model = new PackageModel
        {
            Name = "Test",
            Manufacturer = "Test",
            Version = new Version(1, 0, 0),
            Shortcuts = new List<ShortcutModel>
            {
                new()
                {
                    Name = "My App",
                    TargetFile = "app.exe",
                    Locations = [ShortcutLocation.Desktop, ShortcutLocation.StartMenu],
                    Arguments = "--start",
                    Description = "Launch app",
                    IconFile = "app.ico",
                    WorkingDirectory = "INSTALLDIR",
                    StartMenuSubfolder = "My Company"
                }
            }
        };

        var result = MsiImporter.FromPackageModel(model);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Shortcuts);
        var sc = result.Value.Shortcuts[0];
        Assert.Equal("My App", sc.Name);
        Assert.Equal("app.exe", sc.TargetFile);
        Assert.True(sc.Desktop);
        Assert.True(sc.StartMenu);
        Assert.False(sc.Startup);
        Assert.Equal("--start", sc.Arguments);
        Assert.Equal("Launch app", sc.Description);
        Assert.Equal("app.ico", sc.IconFile);
        Assert.Equal("INSTALLDIR", sc.WorkingDirectory);
        Assert.Equal("My Company", sc.StartMenuSubfolder);
    }

    [Fact]
    public void FromPackageModel_MapsEnvironmentVariables()
    {
        var model = new PackageModel
        {
            Name = "Test",
            Manufacturer = "Test",
            Version = new Version(1, 0, 0),
            EnvironmentVariables = new List<EnvironmentVariableModel>
            {
                new()
                {
                    Name = "MY_VAR",
                    Value = "/some/path",
                    Action = EnvironmentVariableAction.Append,
                    IsSystem = false
                }
            }
        };

        var result = MsiImporter.FromPackageModel(model);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Environment);
        var env = result.Value.Environment[0];
        Assert.Equal("MY_VAR", env.Name);
        Assert.Equal("/some/path", env.Value);
        Assert.Equal("Append", env.Action);
        Assert.False(env.IsSystem);
    }

    [Fact]
    public void FromPackageModel_MapsCustomActions()
    {
        var model = new PackageModel
        {
            Name = "Test",
            Manufacturer = "Test",
            Version = new Version(1, 0, 0),
            CustomActions = new List<CustomActionModel>
            {
                new()
                {
                    Id = "CA_Install",
                    Type = 1,
                    SourceRef = "MyBinary",
                    Target = "EntryPoint",
                    Condition = "NOT Installed",
                    Sequence = 5000,
                    After = "InstallFiles"
                }
            }
        };

        var result = MsiImporter.FromPackageModel(model);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.CustomActions);
        var ca = result.Value.CustomActions[0];
        Assert.Equal("CA_Install", ca.Id);
        Assert.Equal("MyBinary", ca.Source);
        Assert.Equal("EntryPoint", ca.Target);
        Assert.Equal("NOT Installed", ca.Condition);
        Assert.Equal(5000, ca.Sequence);
        Assert.Equal("InstallFiles", ca.After);
        Assert.Null(ca.Before);
    }

    [Fact]
    public void FromPackageModel_SetsProjectTypeToMsi()
    {
        var model = CreateMinimalModel();

        var result = MsiImporter.FromPackageModel(model);

        Assert.True(result.IsSuccess);
        Assert.Equal("msi", result.Value.ProjectType);
    }

    [Fact]
    public void FromPackageModel_MapsInstallDirectory()
    {
        var installDir = KnownFolder.ProgramFiles / "TestApp";
        var model = new PackageModel
        {
            Name = "Test",
            Manufacturer = "Test",
            Version = new Version(1, 0, 0),
            DefaultInstallDirectory = installDir
        };

        var result = MsiImporter.FromPackageModel(model);

        Assert.True(result.IsSuccess);
        Assert.Equal(installDir.ToString(), result.Value.InstallDirectory);
    }

    [Fact]
    public void FromPackageModel_NullInstallDirectory_MapsToNull()
    {
        var model = CreateMinimalModel();

        var result = MsiImporter.FromPackageModel(model);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.InstallDirectory);
    }

    [Fact]
    public void FromPackageModel_EmptyModel_ReturnsEmptyCollections()
    {
        var model = CreateMinimalModel();

        var result = MsiImporter.FromPackageModel(model);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Features);
        Assert.Empty(result.Value.Registry);
        Assert.Empty(result.Value.Services);
        Assert.Empty(result.Value.Shortcuts);
        Assert.Empty(result.Value.Environment);
        Assert.Empty(result.Value.CustomActions);
    }

    private static PackageModel CreateMinimalModel(InstallScope scope = InstallScope.PerMachine)
    {
        return new PackageModel
        {
            Name = "Test",
            Manufacturer = "Test Manufacturer",
            Version = new Version(1, 0, 0),
            Scope = scope
        };
    }
}
