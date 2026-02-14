using FalkInstaller.Models;
using Xunit;

namespace FalkInstaller.Core.Tests;

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
}
