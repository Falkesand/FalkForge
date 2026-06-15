using FalkForge.Studio.Export;
using FalkForge.Studio.Project;
using Xunit;

namespace FalkForge.Studio.Tests.Export;

public class CSharpExporterTests
{
    private static StudioProject CreateMinimalProject()
    {
        return new StudioProject
        {
            Product = new ProductSection
            {
                Name = "TestApp",
                Manufacturer = "TestCorp",
                Version = "1.0.0"
            }
        };
    }

    [Fact]
    public void Export_MinimalProject_ReturnsValidScript()
    {
        var project = CreateMinimalProject();

        var result = CSharpExporter.Export(project);

        Assert.True(result.IsSuccess);
        Assert.Contains("using FalkForge;", result.Value);
        Assert.Contains("using FalkForge.Compiler.Msi;", result.Value);
        Assert.Contains("Installer.Build(args, package =>", result.Value);
        Assert.Contains("package.Name = \"TestApp\";", result.Value);
        Assert.Contains("package.Manufacturer = \"TestCorp\";", result.Value);
        Assert.Contains("new Version(1, 0, 0)", result.Value);
        Assert.Contains("new MsiCompiler());", result.Value);
    }

    [Fact]
    public void Export_EmptyName_ReturnsFailure()
    {
        var project = CreateMinimalProject();
        project.Product.Name = "";

        var result = CSharpExporter.Export(project);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void Export_EmptyManufacturer_ReturnsFailure()
    {
        var project = CreateMinimalProject();
        project.Product.Manufacturer = "";

        var result = CSharpExporter.Export(project);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void Export_WithUpgradeCode_EmitsGuid()
    {
        var project = CreateMinimalProject();
        project.Product.UpgradeCode = "D4E8F1A2-7B3C-4D9E-8A5F-1C6E2B9D4F7A";

        var result = CSharpExporter.Export(project);

        Assert.True(result.IsSuccess);
        Assert.Contains("new Guid(\"D4E8F1A2-7B3C-4D9E-8A5F-1C6E2B9D4F7A\")", result.Value);
    }

    [Fact]
    public void Export_WithDescription_EmitsDescription()
    {
        var project = CreateMinimalProject();
        project.Product.Description = "A test application";

        var result = CSharpExporter.Export(project);

        Assert.True(result.IsSuccess);
        Assert.Contains("package.Description = \"A test application\";", result.Value);
    }

    [Fact]
    public void Export_WithInstallDirectory_EmitsDirectory()
    {
        var project = CreateMinimalProject();
        project.InstallDirectory = "TestCorp\\TestApp";

        var result = CSharpExporter.Export(project);

        Assert.True(result.IsSuccess);
        Assert.Contains("KnownFolder.ProgramFiles / \"TestCorp\\\\TestApp\"", result.Value);
    }

    [Fact]
    public void Export_WithFeature_EmitsFeatureBlock()
    {
        var project = CreateMinimalProject();
        project.Features.Add(new FeatureSection
        {
            Id = "Main",
            Title = "Main Feature",
            IsDefault = true,
            IsRequired = true
        });

        var result = CSharpExporter.Export(project);

        Assert.True(result.IsSuccess);
        Assert.Contains("package.Feature(\"Main\", f =>", result.Value);
        Assert.Contains("f.Title = \"Main Feature\";", result.Value);
        Assert.Contains("f.IsRequired = true;", result.Value);
    }

    [Fact]
    public void Export_WithFeatureFiles_EmitsFilesBlock()
    {
        var project = CreateMinimalProject();
        project.Features.Add(new FeatureSection
        {
            Id = "Main",
            Title = "Main",
            Files = [new FileEntry { Source = "payload/app.exe" }]
        });

        var result = CSharpExporter.Export(project);

        Assert.True(result.IsSuccess);
        Assert.Contains("f.Files(files => files", result.Value);
        Assert.Contains(".Add(\"payload/app.exe\")", result.Value);
    }

    [Fact]
    public void Export_WithNestedFeature_EmitsNestedBlock()
    {
        var project = CreateMinimalProject();
        project.Features.Add(new FeatureSection
        {
            Id = "Parent",
            Title = "Parent",
            Features =
            [
                new FeatureSection { Id = "Child", Title = "Child Feature" }
            ]
        });

        var result = CSharpExporter.Export(project);

        Assert.True(result.IsSuccess);
        Assert.Contains("f.Feature(\"Child\", f =>", result.Value);
        Assert.Contains("f.Title = \"Child Feature\";", result.Value);
    }

    [Fact]
    public void Export_WithRegistry_EmitsRegistryBlock()
    {
        var project = CreateMinimalProject();
        project.Registry.Add(new RegistryEntrySection
        {
            Root = "LocalMachine",
            Key = @"Software\TestCorp",
            ValueName = "InstallPath",
            Value = "[INSTALLFOLDER]"
        });

        var result = CSharpExporter.Export(project);

        Assert.True(result.IsSuccess);
        Assert.Contains("package.Registry(r => r", result.Value);
        Assert.Contains("RegistryRoot.LocalMachine", result.Value);
        Assert.Contains(@"k.Value(""InstallPath"", ""[INSTALLFOLDER]"");", result.Value);
    }

    [Fact]
    public void Export_WithDWordRegistry_EmitsDWord()
    {
        var project = CreateMinimalProject();
        project.Registry.Add(new RegistryEntrySection
        {
            Root = "LocalMachine",
            Key = @"Software\TestCorp",
            ValueName = "Version",
            ValueType = "DWord",
            Value = "42"
        });

        var result = CSharpExporter.Export(project);

        Assert.True(result.IsSuccess);
        Assert.Contains("k.DWord(\"Version\", 42);", result.Value);
    }

    [Fact]
    public void Export_WithService_EmitsServiceBlock()
    {
        var project = CreateMinimalProject();
        project.Services.Add(new ServiceSection
        {
            Name = "TestSvc",
            DisplayName = "Test Service",
            Executable = "svc.exe",
            Description = "A test service"
        });

        var result = CSharpExporter.Export(project);

        Assert.True(result.IsSuccess);
        Assert.Contains("package.Service(\"TestSvc\", svc =>", result.Value);
        Assert.Contains("svc.DisplayName = \"Test Service\";", result.Value);
        Assert.Contains("svc.Executable = \"svc.exe\";", result.Value);
        Assert.Contains("svc.Description = \"A test service\";", result.Value);
    }

    [Fact]
    public void Export_WithShortcut_EmitsShortcutChain()
    {
        var project = CreateMinimalProject();
        project.Shortcuts.Add(new ShortcutSection
        {
            Name = "My App",
            TargetFile = "app.exe",
            Desktop = true,
            StartMenu = true,
            StartMenuSubfolder = "My Company"
        });

        var result = CSharpExporter.Export(project);

        Assert.True(result.IsSuccess);
        Assert.Contains("package.Shortcut(\"My App\", \"app.exe\")", result.Value);
        Assert.Contains(".OnDesktop()", result.Value);
        Assert.Contains(".OnStartMenu(\"My Company\")", result.Value);
    }

    [Fact]
    public void Export_WithEnvironmentVariable_EmitsEnvBlock()
    {
        var project = CreateMinimalProject();
        project.Environment.Add(new EnvironmentVariableSection
        {
            Name = "MY_VAR",
            Value = "hello",
            Action = "Set",
            IsSystem = true
        });

        var result = CSharpExporter.Export(project);

        Assert.True(result.IsSuccess);
        Assert.Contains("package.EnvironmentVariable(\"MY_VAR\", \"hello\", ev =>", result.Value);
    }

    [Fact]
    public void Export_WithAppendEnvVar_EmitsAppendAction()
    {
        var project = CreateMinimalProject();
        project.Environment.Add(new EnvironmentVariableSection
        {
            Name = "PATH",
            Value = "[INSTALLFOLDER]",
            Action = "Append",
            IsSystem = true
        });

        var result = CSharpExporter.Export(project);

        Assert.True(result.IsSuccess);
        Assert.Contains("ev.Action = EnvironmentVariableAction.Append;", result.Value);
    }

    [Fact]
    public void Export_EmptySections_OmitsEmptyBlocks()
    {
        var project = CreateMinimalProject();
        // All lists are empty by default

        var result = CSharpExporter.Export(project);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("package.Feature(", result.Value);
        Assert.DoesNotContain("package.Registry(", result.Value);
        Assert.DoesNotContain("package.Service(", result.Value);
        Assert.DoesNotContain("package.Shortcut(", result.Value);
        Assert.DoesNotContain("package.EnvironmentVariable(", result.Value);
    }

    [Fact]
    public void Export_StringWithQuotes_EscapesCorrectly()
    {
        var project = CreateMinimalProject();
        project.Product.Description = "It's a \"test\" app";

        var result = CSharpExporter.Export(project);

        Assert.True(result.IsSuccess);
        Assert.Contains("\"It's a \\\"test\\\" app\"", result.Value);
    }

    [Fact]
    public void Export_StringWithLineSeparator_EscapesAsUnicode()
    {
        // WHY: U+2028 (LINE SEPARATOR) is a legal runtime character but terminates a C#
        // double-quoted string literal in source. If the exporter emits it verbatim, the
        // generated .csx will not compile, so it must be escaped as a 2028 sequence. The input is
        // built via a C# \u escape so this test source file itself stays plain ASCII.
        var project = CreateMinimalProject();
        project.Product.Description = "before2028after";

        var result = CSharpExporter.Export(project);

        Assert.True(result.IsSuccess);
        Assert.Contains("before\\u2028after", result.Value);
        // The raw separator must NOT survive into the emitted literal.
        Assert.DoesNotContain("before2028after", result.Value);
    }

    [Fact]
    public void Export_StringWithControlChar_EscapesAsUnicode()
    {
        // WHY: a control character below U+0020 (here U+0001) is legal at runtime but corrupts
        // a C# source string literal. The exporter must escape it as a 0001 sequence so the generated
        // script still compiles. The input control char is built via a C# \u escape.
        var project = CreateMinimalProject();
        project.Product.Description = "a0001b";

        var result = CSharpExporter.Export(project);

        Assert.True(result.IsSuccess);
        Assert.Contains("a\\u0001b", result.Value);
        Assert.DoesNotContain("a0001b", result.Value);
    }

    [Fact]
    public void Export_StringWithBackslash_EscapesCorrectly()
    {
        var project = CreateMinimalProject();
        project.Registry.Add(new RegistryEntrySection
        {
            Root = "LocalMachine",
            Key = @"Software\Test",
            ValueName = "Path",
            Value = @"C:\Program Files\App"
        });

        var result = CSharpExporter.Export(project);

        Assert.True(result.IsSuccess);
        Assert.Contains(@"C:\\Program Files\\App", result.Value);
    }

    [Fact]
    public void Export_PerUserScope_EmitsScope()
    {
        var project = CreateMinimalProject();
        project.Product.Scope = "perUser";

        var result = CSharpExporter.Export(project);

        Assert.True(result.IsSuccess);
        Assert.Contains("InstallScope.PerUser", result.Value);
    }

    [Fact]
    public void Export_DefaultScope_OmitsScope()
    {
        var project = CreateMinimalProject();
        // Default is perMachine

        var result = CSharpExporter.Export(project);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain("InstallScope", result.Value);
    }

    [Fact]
    public void Export_NonDefaultArchitecture_EmitsArchitecture()
    {
        var project = CreateMinimalProject();
        project.Product.Architecture = "x86";

        var result = CSharpExporter.Export(project);

        Assert.True(result.IsSuccess);
        Assert.Contains("ProcessorArchitecture.X86", result.Value);
    }

    [Fact]
    public void Export_FeatureNotDefault_EmitsIsDefaultFalse()
    {
        var project = CreateMinimalProject();
        project.Features.Add(new FeatureSection
        {
            Id = "Optional",
            Title = "Optional Feature",
            IsDefault = false
        });

        var result = CSharpExporter.Export(project);

        Assert.True(result.IsSuccess);
        Assert.Contains("f.IsDefault = false;", result.Value);
    }

    [Fact]
    public void Export_ServiceNonDefaultStartMode_EmitsStartMode()
    {
        var project = CreateMinimalProject();
        project.Services.Add(new ServiceSection
        {
            Name = "ManualSvc",
            DisplayName = "Manual Service",
            Executable = "svc.exe",
            StartMode = "Manual"
        });

        var result = CSharpExporter.Export(project);

        Assert.True(result.IsSuccess);
        Assert.Contains("svc.StartMode = ServiceStartMode.Manual;", result.Value);
    }

    [Fact]
    public void Export_UserEnvVar_EmitsIsSystemFalse()
    {
        var project = CreateMinimalProject();
        project.Environment.Add(new EnvironmentVariableSection
        {
            Name = "USER_VAR",
            Value = "val",
            IsSystem = false
        });

        var result = CSharpExporter.Export(project);

        Assert.True(result.IsSuccess);
        Assert.Contains("ev.IsSystem = false;", result.Value);
    }
}
