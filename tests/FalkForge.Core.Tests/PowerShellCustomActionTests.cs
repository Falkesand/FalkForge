using FalkForge.Models;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Core.Tests;

public sealed class PowerShellCustomActionTests
{
    [Fact]
    public void PowerShellScript_SetsExeInDirWithPowerShellCommand()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CustomAction("CA_RunPS", ca =>
            {
                ca.PowerShellScript("Write-Host 'Hello'");
            });
        });

        var action = package.CustomActions[0];
        Assert.Equal(CustomActionType.ExeInDir, action.Type);
        Assert.Equal("SystemFolder", action.SourceRef);
        Assert.Contains("powershell.exe", action.Target);
        Assert.Contains("-NoProfile", action.Target);
        Assert.Contains("-NonInteractive", action.Target);
        Assert.Contains("-ExecutionPolicy Bypass", action.Target);
        Assert.Contains("Write-Host 'Hello'", action.Target);
    }

    [Fact]
    public void PowerShellScript_EscapesDoubleQuotes()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CustomAction("CA_Quoted", ca =>
            {
                ca.PowerShellScript("Write-Host \"Hello World\"");
            });
        });

        var action = package.CustomActions[0];
        Assert.Contains("\\\"Hello World\\\"", action.Target);
    }

    [Fact]
    public void PowerShellFile_ReadsAndEmbedsContent()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.ps1");
        try
        {
            File.WriteAllText(tempFile, "Get-Service | Stop-Service");

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "App";
                p.Manufacturer = "Corp";
                p.CustomAction("CA_PSFile", ca =>
                {
                    ca.PowerShellFile(tempFile);
                });
            });

            var action = package.CustomActions[0];
            Assert.Equal(CustomActionType.ExeInDir, action.Type);
            Assert.Equal("SystemFolder", action.SourceRef);
            Assert.Contains("Get-Service | Stop-Service", action.Target);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void PowerShellFile_ThrowsWhenFileNotFound()
    {
        Assert.Throws<FileNotFoundException>(() =>
            InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "App";
                p.Manufacturer = "Corp";
                p.CustomAction("CA_Missing", ca =>
                {
                    ca.PowerShellFile(@"C:\nonexistent\script.ps1");
                });
            }));
    }

    [Fact]
    public void PowerShellScript_ThrowsOnNullOrWhitespace()
    {
        Assert.Throws<ArgumentException>(() =>
            InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "App";
                p.Manufacturer = "Corp";
                p.CustomAction("CA_Empty", ca =>
                {
                    ca.PowerShellScript("   ");
                });
            }));
    }

    [Fact]
    public void PowerShellScript_ChainsWithDeferred()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CustomAction("CA_DeferredPS", ca =>
            {
                ca.PowerShellScript("Set-Content -Path C:\\test.txt -Value OK")
                    .Deferred()
                    .NoImpersonate();
            });
        });

        var action = package.CustomActions[0];
        var expectedType = CustomActionType.ExeInDir
                         | CustomActionType.InScript
                         | CustomActionType.NoImpersonate;
        Assert.Equal(expectedType, action.Type);
        Assert.Contains("powershell.exe", action.Target);
    }
}
