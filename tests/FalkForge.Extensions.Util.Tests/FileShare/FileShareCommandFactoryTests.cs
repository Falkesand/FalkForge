using System.Text;
using FalkForge.Extensions.Util.FileShare;
using Xunit;

namespace FalkForge.Extensions.Util.Tests.FileShare;

/// <summary>
/// Command-generation tests for <see cref="FileShareCommandFactory"/>. Commands run in a deferred,
/// elevated (SYSTEM) custom action, so the share name, path, description and grant account names are
/// all untrusted-input surfaces that must be safely single-quoted before reaching the script.
/// </summary>
public sealed class FileShareCommandFactoryTests
{
    private static FileShareModel Model(
        string id = "Data",
        string name = "AppData",
        string? description = null,
        string directory = @"C:\Data",
        IReadOnlyList<FileSharePermission>? permissions = null)
        => new()
        {
            Id = id,
            Name = name,
            Description = description,
            Directory = directory,
            Permissions = permissions ?? [],
        };

    private static string DecodeScript(string command)
    {
        const string marker = "-EncodedCommand ";
        int idx = command.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(idx >= 0, $"command is not an -EncodedCommand invocation: {command}");
        string base64 = command[(idx + marker.Length)..].Trim();
        return Encoding.Unicode.GetString(Convert.FromBase64String(base64));
    }

    [Fact]
    public void BasicShare_ProducesCreateInstallAndRemoveRollbackUninstall()
    {
        var steps = FileShareCommandFactory.BuildSteps([Model()]);

        var step = Assert.Single(steps);
        Assert.Equal("Fsh_Data", step.Id);

        string install = DecodeScript(step.InstallCommand);
        Assert.Contains("New-SmbShare -Name 'AppData'", install, StringComparison.Ordinal);
        Assert.Contains(@"-Path 'C:\Data'", install, StringComparison.Ordinal);

        Assert.NotNull(step.RollbackCommand);
        Assert.NotNull(step.UninstallCommand);
        Assert.Contains("Remove-SmbShare -Name 'AppData'", DecodeScript(step.RollbackCommand!), StringComparison.Ordinal);
        Assert.Contains("Remove-SmbShare -Name 'AppData'", DecodeScript(step.UninstallCommand!), StringComparison.Ordinal);
    }

    [Fact]
    public void Description_IsIncludedWhenPresent()
    {
        var steps = FileShareCommandFactory.BuildSteps([Model(description: "App data share")]);
        string install = DecodeScript(steps[0].InstallCommand);
        Assert.Contains("-Description 'App data share'", install, StringComparison.Ordinal);
    }

    [Fact]
    public void Permissions_GroupByLevelIntoAccessListParameters()
    {
        var steps = FileShareCommandFactory.BuildSteps(
        [
            Model(permissions:
            [
                new FileSharePermission { User = "DOMAIN\\Alice", Permission = FileSharePermissionLevel.Full },
                new FileSharePermission { User = "DOMAIN\\Bob", Permission = FileSharePermissionLevel.Read },
                new FileSharePermission { User = "DOMAIN\\Carol", Permission = FileSharePermissionLevel.Change },
                new FileSharePermission { User = "DOMAIN\\Dave", Permission = FileSharePermissionLevel.Full },
            ]),
        ]);

        string install = DecodeScript(steps[0].InstallCommand);
        Assert.Contains("-FullAccess 'DOMAIN\\Alice','DOMAIN\\Dave'", install, StringComparison.Ordinal);
        Assert.Contains("-ChangeAccess 'DOMAIN\\Carol'", install, StringComparison.Ordinal);
        Assert.Contains("-ReadAccess 'DOMAIN\\Bob'", install, StringComparison.Ordinal);
    }

    [Fact]
    public void EncodedCommand_TransportCarriesNoQuoteOrShellMetacharacters()
    {
        var steps = FileShareCommandFactory.BuildSteps([Model(name: "a'\"; calc; [X] & B")]);
        string command = steps[0].InstallCommand;

        const string marker = "-EncodedCommand ";
        string payload = command[(command.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..];
        Assert.DoesNotContain('"', payload);
        Assert.DoesNotContain('\'', payload);
        Assert.DoesNotContain('[', payload);
        Assert.DoesNotContain(';', payload);
        Assert.Matches("^[A-Za-z0-9+/=]+$", payload);
    }

    [Fact]
    public void SingleQuoteInName_IsDoubledSoItCannotBreakOutOfTheLiteral()
    {
        var malicious = "a'; Start-Process calc.exe; '";
        var steps = FileShareCommandFactory.BuildSteps([Model(name: malicious)]);

        string install = DecodeScript(steps[0].InstallCommand);
        Assert.Contains("-Name 'a''; Start-Process calc.exe; '''", install, StringComparison.Ordinal);
    }

    [Fact]
    public void DirectoryWithSpacesAndQuote_IsQuotedSafely()
    {
        var steps = FileShareCommandFactory.BuildSteps([Model(directory: @"C:\Program' Files\Share")]);
        string install = DecodeScript(steps[0].InstallCommand);
        Assert.Contains("-Path 'C:\\Program'' Files\\Share'", install, StringComparison.Ordinal);
    }

    [Fact]
    public void IdWithIllegalChars_IsSanitizedToValidIdentifier()
    {
        var steps = FileShareCommandFactory.BuildSteps([Model(id: "share dir!")]);
        Assert.Equal("Fsh_share_dir_", steps[0].Id);
    }

    [Fact]
    public void InterpreterIsFullyQualified_NotABarePowershellExe()
    {
        var install = FileShareCommandFactory.BuildSteps([Model()])[0].InstallCommand;
        Assert.StartsWith("[SystemFolder]WindowsPowerShell\\v1.0\\powershell.exe", install, StringComparison.Ordinal);
    }
}
