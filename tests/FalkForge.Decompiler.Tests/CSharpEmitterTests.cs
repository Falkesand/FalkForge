using FalkForge.Models;
using Xunit;

namespace FalkForge.Decompiler.Tests;

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

        // FeatureBuilder exposes Title/Description/IsRequired as settable properties,
        // not fluent methods — the prior expectations (f.Title("..."), f.Required())
        // encoded code that did not compile against the real builder API.
        Assert.Contains("builder.Feature(\"Main\"", source);
        Assert.Contains("f.Title = \"Main Feature\";", source);
        Assert.Contains("f.Description = \"The main feature\";", source);
        Assert.Contains("f.IsRequired = true;", source);
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

        // ServiceBuilder exposes DisplayName/Executable/Description as settable
        // properties, not fluent methods — the prior method-call expectations did
        // not compile against the real builder API.
        Assert.Contains("builder.Service(\"MySvc\"", source);
        Assert.Contains("s.DisplayName = \"My Service\";", source);
        Assert.Contains("s.Executable = \"svc.exe\";", source);
        Assert.Contains("s.Description = \"A service\";", source);
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

        // ShortcutBuilder registers a shortcut via a terminal OnDesktop/OnStartMenu/
        // OnStartup call (not .Add()), with metadata set via WithDescription/WithArguments.
        // The prior .Location(...)/.Add() expectations did not compile.
        Assert.Contains("builder.Shortcut(\"My App\", \"app.exe\")", source);
        Assert.Contains(".WithDescription(\"Launch the app\")", source);
        Assert.Contains(".OnDesktop();", source);
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

        Assert.Contains("using FalkForge;", source);
        Assert.Contains("using FalkForge.Builders;", source);
        Assert.Contains("using FalkForge.Models;", source);
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

    [Fact]
    public void Emit_PerUserScope_EmitsScope()
    {
        // Mutation: != PerMachine always-false → PerUser scope omitted
        var model = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            Scope = InstallScope.PerUser
        };
        var source = new CSharpEmitter().Emit(model);
        Assert.Contains("InstallScope.PerUser", source);
    }

    [Fact]
    public void Emit_PerMachineScope_DoesNotEmitScope()
    {
        // PerMachine is default — should NOT emit scope line
        var model = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            Scope = InstallScope.PerMachine
        };
        var source = new CSharpEmitter().Emit(model);
        Assert.DoesNotContain("InstallScope", source);
    }

    [Fact]
    public void Emit_X86Architecture_EmitsArchitecture()
    {
        // Mutation: != X64 always-false → X86 not emitted
        var model = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            Architecture = ProcessorArchitecture.X86
        };
        var source = new CSharpEmitter().Emit(model);
        Assert.Contains("ProcessorArchitecture.X86", source);
    }

    [Fact]
    public void Emit_X64Architecture_DoesNotEmitArchitecture()
    {
        // X64 is default — should NOT emit architecture line
        var model = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            Architecture = ProcessorArchitecture.X64
        };
        var source = new CSharpEmitter().Emit(model);
        Assert.DoesNotContain("ProcessorArchitecture", source);
    }

    [Fact]
    public void Emit_WithUpgradeCode_EmitsGuid()
    {
        var guid = new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        var model = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            UpgradeCode = guid
        };
        var source = new CSharpEmitter().Emit(model);
        Assert.Contains(guid.ToString(), source);
    }

    [Fact]
    public void Emit_EmptyUpgradeCode_DoesNotEmitUpgradeCode()
    {
        var model = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            UpgradeCode = Guid.Empty
        };
        var source = new CSharpEmitter().Emit(model);
        Assert.DoesNotContain("UpgradeCode", source);
    }

    [Fact]
    public void Emit_NullDescription_DoesNotEmitDescription()
    {
        var model = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            Description = null
        };
        var source = new CSharpEmitter().Emit(model);
        Assert.DoesNotContain("Description", source);
    }

    [Fact]
    public void Emit_WithDescriptionValue_EmitsDescription()
    {
        var model = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            Description = "My app"
        };
        var source = new CSharpEmitter().Emit(model);
        Assert.Contains("builder.Description = \"My app\"", source);
    }

    [Fact]
    public void Emit_RequiredFeature_EmitsRequired()
    {
        var model = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            Features = [new FeatureModel { Id = "Core", Title = "Core", IsRequired = true }]
        };
        var source = new CSharpEmitter().Emit(model);
        Assert.Contains("f.IsRequired = true;", source);
    }

    [Fact]
    public void Emit_NonRequiredFeature_DoesNotEmitRequired()
    {
        var model = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            Features = [new FeatureModel { Id = "Opt", Title = "Optional", IsRequired = false }]
        };
        var source = new CSharpEmitter().Emit(model);
        Assert.DoesNotContain("f.IsRequired = true;", source);
    }

    [Fact]
    public void Emit_WithFile_EmitsPayloadAddAndInstallPath()
    {
        // WHY: a migrated installer must repackage each payload file at its
        // original install location. Dropping Files (the prior bug) produced an
        // installer that shipped zero payload — silently broken.
        var model = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            Files =
            [
                new FileEntryModel
                {
                    SourcePath = "foo.dll",
                    TargetDirectory = KnownFolder.ProgramFiles / "Demo" / "App",
                    FileName = "foo.dll"
                }
            ]
        };

        var source = new CSharpEmitter().Emit(model);

        Assert.Contains(".Add(\"payload/Demo/App/foo.dll\")", source);
        Assert.Contains("KnownFolder.ProgramFiles / \"Demo\" / \"App\"", source);
    }

    [Fact]
    public void Emit_RootLevelFile_EmitsPayloadKeyWithoutSubdir()
    {
        // WHY: a file installed directly under the root known folder (no subdir
        // segments) must still get a payload key — "payload/<fileName>".
        var model = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            Files =
            [
                new FileEntryModel
                {
                    SourcePath = "root.txt",
                    TargetDirectory = KnownFolder.ProgramFiles / string.Empty,
                    FileName = "root.txt"
                }
            ]
        };

        var source = new CSharpEmitter().Emit(model);

        Assert.Contains(".Add(\"payload/root.txt\")", source);
    }

    [Fact]
    public void Emit_UnknownRootToken_Throws()
    {
        // WHY: emitting an unmapped known-folder token would produce non-compiling
        // C# (no matching KnownFolder static member). Fail loud instead of shipping
        // a project that won't build.
        var unknownRoot = UnknownRootInstallPath();
        var model = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            Files =
            [
                new FileEntryModel
                {
                    SourcePath = "x.dll",
                    TargetDirectory = unknownRoot,
                    FileName = "x.dll"
                }
            ]
        };

        Assert.Throws<InvalidOperationException>(() => new CSharpEmitter().Emit(model));
    }

    [Fact]
    public void Emit_WildcardFile_DoesNotEmitWildcardAdd()
    {
        // WHY: FileName "*" is a FromDirectory marker, not a real payload file.
        // Emitting .Add("*") would create a meaningless, non-resolvable payload key.
        var model = new PackageModel
        {
            Name = "App", Manufacturer = "Corp", Version = new Version(1, 0, 0),
            Files =
            [
                new FileEntryModel
                {
                    SourcePath = "dir",
                    TargetDirectory = KnownFolder.ProgramFiles / "App",
                    FileName = "*"
                }
            ]
        };

        var source = new CSharpEmitter().Emit(model);

        Assert.DoesNotContain(".Add(\"*\")", source);
    }

    private static InstallPath UnknownRootInstallPath()
    {
        // Build an InstallPath whose Root.Token has no KnownFolder member, to exercise
        // the emitter's fail-loud path. KnownFolder has a private ctor, so we forge one
        // via the public this[] indexer on a folder we then can't easily rename —
        // instead use a reflection-free approach: reuse the wildcard test's StartupFolder
        // is mapped, so we need a genuinely unknown token. Create via reflection.
        var ctor = typeof(KnownFolder).GetConstructor(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            binder: null,
            types: [typeof(string), typeof(string)],
            modifiers: null)!;
        var folder = (KnownFolder)ctor.Invoke(["BogusFolder", "Bogus"]);
        return folder / "Sub";
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
