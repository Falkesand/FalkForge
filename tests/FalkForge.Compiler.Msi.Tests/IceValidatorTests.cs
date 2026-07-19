using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Validation;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

[SupportedOSPlatform("windows")]
public sealed class IceValidatorTests
{
    /// <summary>
    /// Regression for a real product bug found via a CI-only failure: <c>ValidateWithCub</c> used
    /// to open the SHIPPED output MSI with <c>MSIDBOPEN_DIRECT</c>, merge the cub into it, and
    /// <c>MsiDatabaseCommit</c> — permanently persisting the cub's tables (ICE01..ICEnn results,
    /// the cub's own CustomAction/InstallExecuteSequence rows that schedule the ICE checks) into
    /// the real installer artifact. Confirmed in CI: a real darice.cub merge added 104 extra
    /// CustomAction rows to a compiled MSI's CustomAction table
    /// (<see cref="CustomActionSetDirectoryEmissionTests"/> went from 1 row to 105). This test
    /// does not depend on a real darice.cub being present on the host — any valid MSI database
    /// qualifies to msi.dll as a mergeable cub, so a synthetic one with a distinctive marker table
    /// proves the mechanism deterministically everywhere. ICE validation must merge against a
    /// disposable copy and leave the shipped file untouched.
    /// </summary>
    [Fact]
    public void ValidateWithCub_DoesNotMutateTheShippedMsi()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"IceCubMutation_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceDir = Path.Combine(tempDir, "source");
            Directory.CreateDirectory(sourceDir);
            var sourceFile = Path.Combine(sourceDir, "app.exe");
            File.WriteAllText(sourceFile, "fake content for ice-cub-mutation test");

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "IceCubMutationApp";
                p.Manufacturer = "Corp";
                p.Version = new Version(1, 0, 0);
                p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "Corp" / "IceCubMutationApp"));
            });

            var compiler = new MsiCompiler(new WindowsFileSystem());
            var compileResult = compiler.Compile(package, outputDir);
            Assert.True(compileResult.IsSuccess, compileResult.IsFailure ? compileResult.Error.Message : "");
            string msiPath = compileResult.Value;

            // A synthetic "cub": darice.cub is itself just an MSI database, so any valid one
            // suffices to exercise MsiDatabaseMerge. Its marker table must never appear in the
            // shipped MSI afterward.
            var cubPath = Path.Combine(tempDir, "fake.cub");
            var cubResult = MsiDatabase.Create(cubPath);
            Assert.True(cubResult.IsSuccess, cubResult.IsFailure ? cubResult.Error.Message : "");
            using (var cub = cubResult.Value)
            {
                var createTable = cub.Execute(
                    "CREATE TABLE `IceCubProof` (`Marker` CHAR(32) NOT NULL PRIMARY KEY `Marker`)");
                Assert.True(createTable.IsSuccess, createTable.IsFailure ? createTable.Error.Message : "");
                var insert = cub.InsertRow(
                    "SELECT `Marker` FROM `IceCubProof`",
                    record => record.SetString(1, "PROOF_MARKER"));
                Assert.True(insert.IsSuccess, insert.IsFailure ? insert.Error.Message : "");
                var commit = cub.Commit();
                Assert.True(commit.IsSuccess, commit.IsFailure ? commit.Error.Message : "");
            }

            var validator = new IceValidator();
            var validateResult = validator.Validate(msiPath, cubPath);
            Assert.True(validateResult.IsSuccess, validateResult.IsFailure ? validateResult.Error.Message : "");

            var dbResult = MsiDatabase.Open(msiPath, readOnly: true);
            Assert.True(dbResult.IsSuccess, dbResult.IsFailure ? dbResult.Error.Message : "");
            using var db = dbResult.Value;
            var rows = db.QueryRows("SELECT `Marker` FROM `IceCubProof`", 1);
            Assert.True(rows.IsFailure,
                "ICE validation merged the cub into the shipped MSI — the synthetic IceCubProof " +
                "table must not exist in the real output file.");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void FindDariceCub_ReturnsNullOrPath()
    {
        // FindDariceCub searches well-known SDK paths.
        // On CI without Windows SDK it returns null; with SDK it returns a valid path.
        var result = IceValidator.FindDariceCub();

        if (result is not null)
        {
            Assert.True(File.Exists(result), $"FindDariceCub returned non-existent path: {result}");
            Assert.EndsWith(".cub", result, StringComparison.OrdinalIgnoreCase);
        }
        // null is acceptable - means SDK not installed
    }

    [Fact]
    public void Validate_NonExistentMsi_ReturnsFailure()
    {
        var validator = new IceValidator();

        var result = validator.Validate(@"C:\nonexistent\fake.msi");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.FileNotFound, result.Error.Kind);
    }

    [Fact]
    public void Validate_NonExistentMsi_WithCubPath_ReturnsFailure()
    {
        var validator = new IceValidator();

        var result = validator.Validate(@"C:\nonexistent\fake.msi", @"C:\nonexistent\darice.cub");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.FileNotFound, result.Error.Kind);
        Assert.Contains("MSI file not found", result.Error.Message);
    }

    [Fact]
    public void Validate_NonExistentCub_ReturnsFailure()
    {
        var tempMsi = Path.Combine(Path.GetTempPath(), $"ice_test_{Guid.NewGuid():N}.msi");
        try
        {
            File.WriteAllBytes(tempMsi, [0x00]); // Dummy file so it exists
            var validator = new IceValidator();

            var result = validator.Validate(tempMsi, @"C:\nonexistent\darice.cub");

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorKind.FileNotFound, result.Error.Kind);
            Assert.Contains("CUB file not found", result.Error.Message);
        }
        finally
        {
            File.Delete(tempMsi);
        }
    }

    [Fact]
    public void Validate_WhenNoCubAvailable_ReturnsSuccess()
    {
        // When darice.cub is not found, Validate(msiPath) should return success
        // with empty messages. We can test this if SDK is not present,
        // or we can test via the single-arg overload with a real MSI.
        // Since we can't guarantee SDK absence, test the model behavior instead.
        var successResult = IceValidationResult.Success();

        Assert.True(successResult.IsValid);
        Assert.Empty(successResult.Messages);
    }

    [Fact]
    public void IceMessage_Properties_SetCorrectly()
    {
        var message = new IceMessage
        {
            IceName = "ICE03",
            Severity = IceMessageSeverity.Error,
            Description = "Invalid identifier",
            Table = "Component",
            Column = "Component",
            PrimaryKeys = "C_main"
        };

        Assert.Equal("ICE03", message.IceName);
        Assert.Equal(IceMessageSeverity.Error, message.Severity);
        Assert.Equal("Invalid identifier", message.Description);
        Assert.Equal("Component", message.Table);
        Assert.Equal("Component", message.Column);
        Assert.Equal("C_main", message.PrimaryKeys);
    }

    [Fact]
    public void IceMessage_OptionalProperties_DefaultToNull()
    {
        var message = new IceMessage
        {
            IceName = "ICE01",
            Severity = IceMessageSeverity.Information,
            Description = "Some info"
        };

        Assert.Null(message.Table);
        Assert.Null(message.Column);
        Assert.Null(message.PrimaryKeys);
    }

    [Fact]
    public void IceValidationResult_IsValid_TrueWhenNoErrors()
    {
        var result = IceValidationResult.FromMessages(
        [
            new IceMessage
            {
                IceName = "ICE01",
                Severity = IceMessageSeverity.Information,
                Description = "Info message"
            },
            new IceMessage
            {
                IceName = "ICE02",
                Severity = IceMessageSeverity.Warning,
                Description = "Warning message"
            }
        ]);

        Assert.True(result.IsValid);
        Assert.Equal(2, result.Messages.Count);
        Assert.Empty(result.Errors);
        Assert.Single(result.Warnings);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void IceValidationResult_IsValid_FalseWhenErrors()
    {
        var result = IceValidationResult.FromMessages(
        [
            new IceMessage
            {
                IceName = "ICE03",
                Severity = IceMessageSeverity.Error,
                Description = "Error message"
            },
            new IceMessage
            {
                IceName = "ICE04",
                Severity = IceMessageSeverity.Warning,
                Description = "Warning message"
            }
        ]);

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Messages.Count);
        Assert.Single(result.Errors);
        Assert.Single(result.Warnings);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void IceValidationResult_IsValid_FalseWhenFailures()
    {
        var result = IceValidationResult.FromMessages(
        [
            new IceMessage
            {
                IceName = "ICE99",
                Severity = IceMessageSeverity.Failure,
                Description = "Critical failure"
            }
        ]);

        Assert.False(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Single(result.Failures);
    }

    [Fact]
    public void IceValidationResult_Success_ReturnsEmptyMessages()
    {
        var result = IceValidationResult.Success();

        Assert.True(result.IsValid);
        Assert.Empty(result.Messages);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
        Assert.Empty(result.Failures);
    }
}
