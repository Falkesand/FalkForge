namespace FalkForge.Engine.Tests.Execution;

using System.Runtime.Versioning;
using System.Text;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Engine.Execution;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// The direct (per-user) path sets a secure property through a runtime transform, not on the command line.
/// These tests compile a real base MSI, run the executor with a secret property, and prove three things
/// while the transform still exists: the secret plaintext never reaches the msiexec command line, the
/// generated transform genuinely sets the property, and the staging directory is deleted afterward.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MsiExecutorSecureTransformTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), $"MsiExecSecure_{Guid.NewGuid():N}");

    public MsiExecutorSecureTransformTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose() => TestTemp.TryDelete(_tempDir);

    private string CompileBaseMsi()
    {
        var sourceDir = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(sourceDir);
        var sourceFile = Path.Combine(sourceDir, "app.exe");
        File.WriteAllText(sourceFile, "payload");
        var outputDir = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(outputDir);

        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "SecretApp";
            p.Manufacturer = "TestCorp";
            p.Version = new Version(1, 0, 0);
            p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "SecretApp"));
        });

        var result = new MsiCompiler().Compile(package, outputDir);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    private static PlanAction ActionFor(string baseMsi, Dictionary<string, SensitiveBytes> secrets,
        Dictionary<string, string>? properties = null) =>
        new()
        {
            PackageId = "TestMsi",
            ActionType = PlanActionType.Install,
            Package = new PackageInfo
            {
                Id = "TestMsi",
                Type = PackageType.MsiPackage,
                DisplayName = "Test MSI",
                SourcePath = baseMsi,
                Sha256Hash = "AABBCCDD"
            },
            Properties = properties ?? new Dictionary<string, string>(),
            SecureProperties = secrets
        };

    [Fact]
    public async Task DirectInstall_SecretProperty_SetViaTransform_NeverOnCommandLine()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var baseMsi = CompileBaseMsi();
        const string password = "P@ss \" & | ; < > w0rd!";

        string? commandLine = null;
        string? valueDuringInstall = null;
        var api = new CapturingMsiApi((path, cmd) =>
        {
            commandLine = cmd;
            valueDuringInstall = ReadTransformedProperty(baseMsi, cmd!, "SQLPASSWORD");
        });

        using var secret = SensitiveBytes.FromPlaintext(Encoding.UTF8.GetBytes(password));
        var action = ActionFor(baseMsi, new Dictionary<string, SensitiveBytes>(StringComparer.OrdinalIgnoreCase)
        {
            ["SQLPASSWORD"] = secret
        });

        var executor = new MsiExecutor(() => null, () => null, () => api);
        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsSuccess);
        Assert.NotNull(commandLine);

        // The transform is on the command line; the secret is not.
        Assert.Contains("TRANSFORMS=\"", commandLine, StringComparison.Ordinal);
        Assert.DoesNotContain(password, commandLine, StringComparison.Ordinal);
        Assert.DoesNotContain("SQLPASSWORD", commandLine, StringComparison.Ordinal);

        // The generated transform genuinely set the property to the exact special-character password.
        Assert.Equal(password, valueDuringInstall);

        // Staging is cleaned up: the transform file named on the command line is gone.
        var mst = ExtractTransformPath(commandLine);
        Assert.False(File.Exists(mst), $"Staging transform was not deleted: {mst}");
    }

    [Fact]
    public async Task DirectInstall_AuthorTransformAndSecret_MergeIntoOneTransformsPair()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var baseMsi = CompileBaseMsi();

        string? commandLine = null;
        var api = new CapturingMsiApi((_, cmd) => commandLine = cmd);

        using var secret = SensitiveBytes.FromPlaintext("hunter2"u8);
        // The author set TRANSFORMS via SetProperty; it flows through as a normal property.
        var action = ActionFor(
            baseMsi,
            new Dictionary<string, SensitiveBytes>(StringComparer.OrdinalIgnoreCase) { ["APIKEY"] = secret },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["TRANSFORMS"] = @"C:\author\lang.mst" });

        var executor = new MsiExecutor(() => null, () => null, () => api);
        var result = await executor.ExecuteAsync(action, CancellationToken.None, new Progress<int>(_ => { }));

        Assert.True(result.IsSuccess);
        Assert.NotNull(commandLine);
        // Exactly one TRANSFORMS pair, and it still carries the author's transform.
        Assert.Equal(1, CountOccurrences(commandLine, "TRANSFORMS=\""));
        Assert.Contains(@"C:\author\lang.mst;", commandLine, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", commandLine, StringComparison.Ordinal);
    }

    private static string ReadTransformedProperty(string baseMsi, string commandLine, string property)
    {
        var mst = ExtractTransformPath(commandLine);
        // Split the author part (before ';') off if present; the secret transform is the last entry.
        var secretMst = mst.Contains(';', StringComparison.Ordinal)
            ? mst[(mst.LastIndexOf(';') + 1)..]
            : mst;

        var applied = Path.Combine(Path.GetDirectoryName(secretMst)!, $"read-{Guid.NewGuid():N}.msi");
        File.Copy(baseMsi, applied);
        using var db = MsiDatabase.Open(applied, readOnly: false).Value;
        Assert.True(db.ApplyTransform(secretMst).IsSuccess);
        var rows = db.QueryRows($"SELECT `Value` FROM `Property` WHERE `Property` = '{property}'", 1);
        Assert.True(rows.IsSuccess);
        return Assert.Single(rows.Value)[0]!;
    }

    private static string ExtractTransformPath(string commandLine)
    {
        const string marker = "TRANSFORMS=\"";
        var start = commandLine.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = commandLine.IndexOf('"', start);
        return commandLine[start..end];
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private sealed class CapturingMsiApi(Action<string, string?> onInstall) : IMsiApi
    {
        public uint InstallProduct(string packagePath, string? commandLine)
        {
            onInstall(packagePath, commandLine);
            return 0;
        }

        public uint ConfigureProduct(string productCode, int installLevel, int installState) => 0;

        public int SetInternalUI(int uiLevel, nint window) => 0;

        public nint SetExternalUI(MsiExternalUIHandler? handler, uint messageFilter, nint context) => nint.Zero;
    }
}
