using FalkForge.Builders;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Core.Tests;

public sealed class InstallerTests
{
    [Fact]
    public void Build_WithValidPackage_ReturnsZero()
    {
        var exitCode = Installer.Build([], p =>
        {
            p.Name = "TestApp";
            p.Manufacturer = "TestCorp";
        });

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Build_WithInvalidPackage_ReturnsOne()
    {
        var exitCode = Installer.Build([], p =>
        {
            p.Name = "";
            p.Manufacturer = "";
        });

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Build_WithCompiler_CallsCompile()
    {
        var compiler = new MockCompiler();

        var exitCode = Installer.Build([], p =>
        {
            p.Name = "TestApp";
            p.Manufacturer = "TestCorp";
        }, compiler);

        Assert.Equal(0, exitCode);
        Assert.NotNull(compiler.LastPackage);
        Assert.Equal("TestApp", compiler.LastPackage.Name);
        Assert.Equal("TestCorp", compiler.LastPackage.Manufacturer);
    }

    [Fact]
    public void Build_WithCompilerFailure_ReturnsOne()
    {
        var compiler = new MockCompiler
        {
            CompileResult = Result<string>.Failure(ErrorKind.CompilationError, "Something went wrong")
        };

        var exitCode = Installer.Build([], p =>
        {
            p.Name = "TestApp";
            p.Manufacturer = "TestCorp";
        }, compiler);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Build_OutputFlag_PassesToCompiler()
    {
        var compiler = new MockCompiler();
        var args = new[] { "-o", "/custom/output" };

        Installer.Build(args, p =>
        {
            p.Name = "TestApp";
            p.Manufacturer = "TestCorp";
        }, compiler);

        Assert.Equal("/custom/output", compiler.LastOutputPath);
    }

    [Fact]
    public void Build_LongOutputFlag_PassesToCompiler()
    {
        var compiler = new MockCompiler();
        var args = new[] { "--output", "/another/path" };

        Installer.Build(args, p =>
        {
            p.Name = "TestApp";
            p.Manufacturer = "TestCorp";
        }, compiler);

        Assert.Equal("/another/path", compiler.LastOutputPath);
    }

    [Fact]
    public void Build_NoOutputFlag_PassesCurrentDirectory()
    {
        var compiler = new MockCompiler();

        Installer.Build([], p =>
        {
            p.Name = "TestApp";
            p.Manufacturer = "TestCorp";
        }, compiler);

        Assert.Equal(Directory.GetCurrentDirectory(), compiler.LastOutputPath);
    }

    [Fact]
    public void Build_InvalidPackage_DoesNotCallCompiler()
    {
        var compiler = new MockCompiler();

        Installer.Build([], p =>
        {
            p.Name = "";
            p.Manufacturer = "";
        }, compiler);

        Assert.Null(compiler.LastPackage);
    }

    // --- Build(args, PackageModel, ICompiler) overload ---

    [Fact]
    public void Build_PrebuiltModel_WithValidModelAndCompiler_ReturnsZeroAndCallsCompiler()
    {
        // A migration program owns a pre-built PackageModel (from the decompiler's emitted builder.Build())
        // and supplies an explicit -o path. It must reach the compiler with that exact output path
        // — the whole point is to skip re-running the builder and hand off directly.
        var model = new PackageBuilder { Name = "MigratedApp", Manufacturer = "Corp" }.Build();
        var compiler = new MockCompiler();
        var args = new[] { "-o", "/migrated/output" };

        var exitCode = Installer.Build(args, model, compiler);

        Assert.Equal(0, exitCode);
        Assert.Equal(model, compiler.LastPackage);
        Assert.Equal("/migrated/output", compiler.LastOutputPath);
    }

    [Fact]
    public void Build_PrebuiltModel_WithCompilerFailure_ReturnsOne()
    {
        var model = new PackageBuilder { Name = "MigratedApp", Manufacturer = "Corp" }.Build();
        var compiler = new MockCompiler
        {
            CompileResult = Result<string>.Failure(ErrorKind.CompilationError, "disk full")
        };

        var exitCode = Installer.Build([], model, compiler);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Build_PrebuiltModel_WithInvalidModel_ReturnsOne()
    {
        // Validation still runs for a prebuilt model — invalid models must not reach the compiler.
        var model = new PackageBuilder { Name = "", Manufacturer = "" }.Build();
        var compiler = new MockCompiler();

        var exitCode = Installer.Build([], model, compiler);

        Assert.Equal(1, exitCode);
        Assert.Null(compiler.LastPackage);
    }

    [Fact]
    public void Build_PrebuiltModel_NullModel_Throws()
    {
        var compiler = new MockCompiler();
        Assert.Throws<ArgumentNullException>((Action)(() => Installer.Build([], (PackageModel)null!, compiler)));
    }

    [Fact]
    public void Build_PrebuiltModel_NullCompiler_Throws()
    {
        var model = new PackageBuilder { Name = "App", Manufacturer = "Corp" }.Build();
        Assert.Throws<ArgumentNullException>((Action)(() => Installer.Build([], model, null!)));
    }

    private sealed class MockCompiler : ICompiler
    {
        public PackageModel? LastPackage { get; private set; }
        public string? LastOutputPath { get; private set; }
        public Result<string> CompileResult { get; set; } = Result<string>.Success("/output/test.msi");

        public Result<string> Compile(PackageModel model, string outputPath)
        {
            LastPackage = model;
            LastOutputPath = outputPath;
            return CompileResult;
        }
    }

    // --- BuildMergeModule ---

    [Fact]
    public void BuildMergeModule_ValidConfig_ReturnsZeroAndPassesModelAndOutputPathToCompile()
    {
        MergeModuleModel? capturedModel = null;
        string? capturedPath = null;
        var args = new[] { "-o", "/msm/output.msm" };

        var exitCode = Installer.BuildMergeModule(args, b => b
                .Id(Guid.NewGuid())
                .Manufacturer("TestCorp")
                .Component("Component1")
                .Dependency("Dependency1"),
            (model, path) =>
            {
                capturedModel = model;
                capturedPath = path;
                return Result<string>.Success("/msm/output.msm");
            });

        Assert.Equal(0, exitCode);
        Assert.NotNull(capturedModel);
        Assert.Equal("TestCorp", capturedModel.Manufacturer);
        Assert.Contains("Component1", capturedModel.Components);
        Assert.Contains("Dependency1", capturedModel.Dependencies);
        Assert.Equal("/msm/output.msm", capturedPath);
    }

    [Fact]
    public void BuildMergeModule_InvalidConfig_ReturnsOneAndDoesNotCallCompile()
    {
        // Manufacturer left empty -- MergeModuleValidator MSM004 must reject before compile runs.
        var compileCalled = false;

        var exitCode = Installer.BuildMergeModule([], _ => { },
            (_, _) =>
            {
                compileCalled = true;
                return Result<string>.Success("/msm/output.msm");
            });

        Assert.Equal(1, exitCode);
        Assert.False(compileCalled);
    }

    [Fact]
    public void BuildMergeModule_CompileFailure_ReturnsOne()
    {
        var exitCode = Installer.BuildMergeModule([], b => b
                .Id(Guid.NewGuid())
                .Manufacturer("TestCorp"),
            (_, _) => Result<string>.Failure(ErrorKind.CompilationError, "disk full"));

        Assert.Equal(1, exitCode);
    }

    // --- BuildPatch ---

    [Fact]
    public void BuildPatch_ValidConfig_ReturnsZeroAndPassesModelAndOutputPathToCompile()
    {
        PatchModel? capturedModel = null;
        string? capturedPath = null;
        var args = new[] { "-o", "/msp/output.msp" };

        var exitCode = Installer.BuildPatch(args, b => b
                .Id(Guid.NewGuid())
                .Classification(PatchClassification.SecurityUpdate)
                .TargetMsi("base.msi")
                .UpdatedMsi("updated.msi"),
            (model, path) =>
            {
                capturedModel = model;
                capturedPath = path;
                return Result<string>.Success("/msp/output.msp");
            });

        Assert.Equal(0, exitCode);
        Assert.NotNull(capturedModel);
        Assert.Equal(PatchClassification.SecurityUpdate, capturedModel.Classification);
        Assert.Equal("base.msi", capturedModel.TargetMsiPath);
        Assert.Equal("updated.msi", capturedModel.UpdatedMsiPath);
        Assert.Equal("/msp/output.msp", capturedPath);
    }

    [Fact]
    public void BuildPatch_InvalidConfig_ReturnsOneAndDoesNotCallCompile()
    {
        // TargetMsiPath/UpdatedMsiPath left empty -- MSP001/MSP002 must reject before compile runs.
        var compileCalled = false;

        var exitCode = Installer.BuildPatch([], _ => { },
            (_, _) =>
            {
                compileCalled = true;
                return Result<string>.Success("/msp/output.msp");
            });

        Assert.Equal(1, exitCode);
        Assert.False(compileCalled);
    }

    [Fact]
    public void BuildPatch_CompileFailure_ReturnsOne()
    {
        var exitCode = Installer.BuildPatch([], b => b
                .Id(Guid.NewGuid())
                .TargetMsi("base.msi")
                .UpdatedMsi("updated.msi"),
            (_, _) => Result<string>.Failure(ErrorKind.CompilationError, "disk full"));

        Assert.Equal(1, exitCode);
    }

    // --- BuildTransform ---

    [Fact]
    public void BuildTransform_ValidConfig_ReturnsZeroAndPassesModelAndOutputPathToCompile()
    {
        TransformModel? capturedModel = null;
        string? capturedPath = null;
        var args = new[] { "-o", "/mst/output.mst" };

        var exitCode = Installer.BuildTransform(args, b => b
                .BaseMsi("base.msi")
                .TargetMsi("target.msi")
                .SetProperty("PRODUCTVERSION", "2.0.0"),
            (model, path) =>
            {
                capturedModel = model;
                capturedPath = path;
                return Result<string>.Success("/mst/output.mst");
            });

        Assert.Equal(0, exitCode);
        Assert.NotNull(capturedModel);
        Assert.Equal("base.msi", capturedModel.BaseMsiPath);
        Assert.Equal("target.msi", capturedModel.TargetMsiPath);
        Assert.Equal("2.0.0", capturedModel.PropertyChanges["PRODUCTVERSION"]);
        Assert.Equal("/mst/output.mst", capturedPath);
    }

    [Fact]
    public void BuildTransform_InvalidConfig_ReturnsOneAndDoesNotCallCompile()
    {
        // BaseMsiPath/TargetMsiPath left empty -- MST001/MST002 must reject before compile runs.
        var compileCalled = false;

        var exitCode = Installer.BuildTransform([], _ => { },
            (_, _) =>
            {
                compileCalled = true;
                return Result<string>.Success("/mst/output.mst");
            });

        Assert.Equal(1, exitCode);
        Assert.False(compileCalled);
    }

    [Fact]
    public void BuildTransform_CompileFailure_ReturnsOne()
    {
        var exitCode = Installer.BuildTransform([], b => b
                .BaseMsi("base.msi")
                .TargetMsi("target.msi"),
            (_, _) => Result<string>.Failure(ErrorKind.CompilationError, "disk full"));

        Assert.Equal(1, exitCode);
    }

    // --- BuildBundle ---

    [Fact]
    public void BuildBundle_Success_ReturnsZeroAndPassesOutputPathToCompile()
    {
        string? capturedPath = null;
        var args = new[] { "-o", "/bundle/output.exe" };

        var exitCode = Installer.BuildBundle(args, path =>
        {
            capturedPath = path;
            return Result<string>.Success("/bundle/output.exe");
        });

        Assert.Equal(0, exitCode);
        Assert.Equal("/bundle/output.exe", capturedPath);
    }

    [Fact]
    public void BuildBundle_Failure_ReturnsOne()
    {
        var exitCode = Installer.BuildBundle([], _ => Result<string>.Failure(ErrorKind.CompilationError, "signing failed"));

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void BuildBundle_NoOutputFlag_PassesCurrentDirectory()
    {
        string? capturedPath = null;

        Installer.BuildBundle([], path =>
        {
            capturedPath = path;
            return Result<string>.Success("/bundle/output.exe");
        });

        Assert.Equal(Directory.GetCurrentDirectory(), capturedPath);
    }

    // --- BuildBundleAsync ---

    [Fact]
    public async Task BuildBundleAsync_Success_ReturnsZeroAndPassesOutputPathToCompile()
    {
        string? capturedPath = null;
        var args = new[] { "-o", "/bundle/async-output.exe" };

        var exitCode = await Installer.BuildBundleAsync(args, path =>
        {
            capturedPath = path;
            return ValueTask.FromResult(Result<string>.Success("/bundle/async-output.exe"));
        });

        Assert.Equal(0, exitCode);
        Assert.Equal("/bundle/async-output.exe", capturedPath);
    }

    [Fact]
    public async Task BuildBundleAsync_Failure_ReturnsOne()
    {
        var exitCode = await Installer.BuildBundleAsync([],
            _ => ValueTask.FromResult(Result<string>.Failure(ErrorKind.CompilationError, "remote signer unreachable")));

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task BuildBundleAsync_NullCompile_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Installer.BuildBundleAsync([], null!));
    }
}
