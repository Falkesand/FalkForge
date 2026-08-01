using System.Runtime.Versioning;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

/// <summary>
/// Round-trip coverage for <see cref="MsmCompiler.Compile"/>: builds a <see cref="MergeModuleModel"/>
/// directly (bypassing <c>MergeModuleBuilder</c>, which is covered separately), compiles it to a real
/// <c>.msm</c>, then re-opens the file with <see cref="MsiDatabase"/> and asserts the emitted rows match
/// the input model. Before this file existed, <c>MsmCompiler</c> had no coverage that verified compiled
/// output at all -- only the pure static helpers (<c>DeterministicComponentGuid</c>, <c>PrefixComponentId</c>)
/// were tested.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MsmCompilerIntegrationTests
{
    private static string CreateTempDir(string prefix)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    [Fact]
    public void Compile_WithDependency_EmitsModuleDependencyRow()
    {
        var tempDir = CreateTempDir("MsmDep");
        try
        {
            var moduleId = Guid.NewGuid();
            var module = new MergeModuleModel
            {
                Id = moduleId,
                Language = 1033,
                Version = new Version(2, 3, 4),
                Manufacturer = "TestCorp",
                Components = ["SharedRuntime"],
                Dependencies = ["SharedRuntime_1.0"]
            };

            var compileResult = new MsmCompiler().Compile(module, tempDir);
            Assert.True(compileResult.IsSuccess,
                $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

            using var db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;

            var rows = db.QueryRows(
                "SELECT `ModuleID`, `ModuleLanguage`, `RequiredID`, `RequiredLanguage`, `RequiredVersion` FROM `ModuleDependency`",
                5).Value;

            var expectedModuleId = moduleId.ToString("N").ToUpperInvariant();
            var row = Assert.Single(rows);
            Assert.Equal(expectedModuleId, row[0]);
            Assert.Equal("1033", row[1]);
            Assert.Equal("SharedRuntime_1.0", row[2]);
            Assert.Equal("1033", row[3]);
            Assert.Null(row[4]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { /* best effort */ }
            }
        }
    }

    [Fact]
    public void Compile_WithMultipleDependencies_EmitsOneRowPerDependency()
    {
        var tempDir = CreateTempDir("MsmDepMulti");
        try
        {
            var module = new MergeModuleModel
            {
                Id = Guid.NewGuid(),
                Language = 1033,
                Version = new Version(1, 0, 0),
                Manufacturer = "TestCorp",
                Components = ["Comp1"],
                Dependencies = ["Dep1", "Dep2"]
            };

            var compileResult = new MsmCompiler().Compile(module, tempDir);
            Assert.True(compileResult.IsSuccess,
                $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

            using var db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;

            var rows = db.QueryRows("SELECT `RequiredID` FROM `ModuleDependency`", 1).Value;

            Assert.Equal(2, rows.Count);
            Assert.Contains(rows, r => r[0] == "Dep1");
            Assert.Contains(rows, r => r[0] == "Dep2");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { /* best effort */ }
            }
        }
    }

    [Fact]
    public void Compile_NoDependencies_ModuleDependencyTableIsEmpty()
    {
        var tempDir = CreateTempDir("MsmDepNone");
        try
        {
            var module = new MergeModuleModel
            {
                Id = Guid.NewGuid(),
                Language = 1033,
                Version = new Version(1, 0, 0),
                Manufacturer = "TestCorp",
                Components = ["Comp1"]
            };

            var compileResult = new MsmCompiler().Compile(module, tempDir);
            Assert.True(compileResult.IsSuccess,
                $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

            using var db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;

            var rows = db.QueryRows("SELECT `RequiredID` FROM `ModuleDependency`", 1).Value;

            Assert.Empty(rows);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { /* best effort */ }
            }
        }
    }

    [Fact]
    public void Compile_ProducesModuleSignatureMatchingModel()
    {
        var tempDir = CreateTempDir("MsmSig");
        try
        {
            var moduleId = Guid.NewGuid();
            var module = new MergeModuleModel
            {
                Id = moduleId,
                Language = 1041,
                Version = new Version(3, 2, 1),
                Manufacturer = "TestCorp",
                Components = ["Comp1"]
            };

            var compileResult = new MsmCompiler().Compile(module, tempDir);
            Assert.True(compileResult.IsSuccess,
                $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

            using var db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;

            var rows = db.QueryRows("SELECT `ModuleID`, `Language`, `Version` FROM `ModuleSignature`", 3).Value;
            var row = Assert.Single(rows);

            Assert.Equal(moduleId.ToString("N").ToUpperInvariant(), row[0]);
            Assert.Equal("1041", row[1]);
            Assert.Equal("3.2.1", row[2]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { /* best effort */ }
            }
        }
    }

    [Fact]
    public void Compile_ProducesModuleComponentsMatchingModel()
    {
        var tempDir = CreateTempDir("MsmComp");
        try
        {
            var moduleId = Guid.NewGuid();
            var module = new MergeModuleModel
            {
                Id = moduleId,
                Language = 1033,
                Version = new Version(1, 0, 0),
                Manufacturer = "TestCorp",
                Components = ["Comp1", "Comp2"]
            };

            var compileResult = new MsmCompiler().Compile(module, tempDir);
            Assert.True(compileResult.IsSuccess,
                $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

            using var db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;

            var moduleGuid = moduleId.ToString("N").ToUpperInvariant();
            var moduleComponentRows = db.QueryRows(
                "SELECT `Component`, `ModuleID`, `Language` FROM `ModuleComponents`", 3).Value;
            var componentRows = db.QueryRows(
                "SELECT `Component`, `Directory_` FROM `Component`", 2).Value;

            Assert.Equal(2, moduleComponentRows.Count);
            Assert.Equal(2, componentRows.Count);

            var expectedComp1 = MsmCompiler.PrefixComponentId(moduleGuid, "Comp1");
            var expectedComp2 = MsmCompiler.PrefixComponentId(moduleGuid, "Comp2");

            Assert.Contains(moduleComponentRows, r => r[0] == expectedComp1 && r[1] == moduleGuid && r[2] == "1033");
            Assert.Contains(moduleComponentRows, r => r[0] == expectedComp2 && r[1] == moduleGuid && r[2] == "1033");
            Assert.Contains(componentRows, r => r[0] == expectedComp1 && r[1] == "TARGETDIR");
            Assert.Contains(componentRows, r => r[0] == expectedComp2 && r[1] == "TARGETDIR");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { /* best effort */ }
            }
        }
    }

    [Fact]
    public void Compile_InvalidModel_ReturnsValidationFailure()
    {
        var tempDir = CreateTempDir("MsmInvalid");
        try
        {
            // Bypass the builder (which already rejects this) to prove MsmCompiler.Compile
            // itself re-validates rather than trusting whatever MergeModuleModel it is handed.
            var module = new MergeModuleModel
            {
                Id = Guid.Empty,
                Language = 1033,
                Version = new Version(1, 0, 0),
                Manufacturer = "TestCorp"
            };

            var compileResult = new MsmCompiler().Compile(module, tempDir);

            Assert.True(compileResult.IsFailure);
            Assert.Equal(ErrorKind.Validation, compileResult.Error.Kind);
            Assert.Contains("MSM001", compileResult.Error.Message);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { /* best effort */ }
            }
        }
    }

    [Fact]
    public void Compile_OutputPathCollidesWithDirectory_ReturnsTypedFailure()
    {
        var tempDir = CreateTempDir("MsmCollide");
        try
        {
            var moduleId = Guid.NewGuid();
            var module = new MergeModuleModel
            {
                Id = moduleId,
                Language = 1033,
                Version = new Version(1, 0, 0),
                Manufacturer = "TestCorp",
                Components = ["Comp1"]
            };

            // Pre-create a directory at the exact path MsmCompiler will try to write the .msm
            // file to. MsiOpenDatabase cannot create a database where a directory already
            // exists, so this exercises the native-error Result.Failure path (step 3 of
            // Compile) without relying on filesystem permissions.
            var moduleGuid = moduleId.ToString("N").ToUpperInvariant();
            var collidingPath = Path.Combine(tempDir, $"MergeModule.{moduleGuid}.msm");
            Directory.CreateDirectory(collidingPath);

            var compileResult = new MsmCompiler().Compile(module, tempDir);

            Assert.True(compileResult.IsFailure);
            Assert.Equal(ErrorKind.CompilationError, compileResult.Error.Kind);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { /* best effort */ }
            }
        }
    }
}
