using FalkForge.Builders;
using FalkForge.Models;
using FalkForge.Testing;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Core.Tests.Builders;

public sealed class PowerShellCustomActionTests
{
    [Fact]
    public void PowerShellScript_SetsExeInDirTypeAndSystemFolder()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CustomAction("CA_PS", ca =>
            {
                ca.PowerShellScript("Write-Host 'Hello'");
            });
        });

        var action = package.CustomActions[0];
        Assert.Equal(CustomActionType.ExeInDir, action.Type);
        Assert.Equal("SystemFolder", action.SourceRef);
    }

    [Fact]
    public void PowerShellScript_ConstructsCorrectCommandLine()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CustomAction("CA_PS", ca =>
            {
                ca.PowerShellScript("Write-Host 'Hello'");
            });
        });

        var target = package.CustomActions[0].Target;
        Assert.NotNull(target);
        Assert.Contains("powershell.exe", target);
        Assert.Contains("-NoProfile", target);
        Assert.Contains("-NonInteractive", target);
        Assert.Contains("-ExecutionPolicy Bypass", target);
        Assert.Contains("-Command", target);
        Assert.Contains("Write-Host 'Hello'", target);
    }

    [Fact]
    public void PowerShellScript_EscapesDoubleQuotes()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CustomAction("CA_PS", ca =>
            {
                ca.PowerShellScript("Write-Host \"Hello World\"");
            });
        });

        var target = package.CustomActions[0].Target!;
        // The inner double quotes should be escaped with backslash
        Assert.Contains("\\\"Hello World\\\"", target);
    }

    [Fact]
    public void PowerShellScript_NullScript_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "App";
                p.Manufacturer = "Corp";
                p.CustomAction("CA_PS", ca =>
                {
                    ca.PowerShellScript(null!);
                });
            }));
    }

    [Fact]
    public void PowerShellScript_EmptyScript_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "App";
                p.Manufacturer = "Corp";
                p.CustomAction("CA_PS", ca =>
                {
                    ca.PowerShellScript("");
                });
            }));
    }

    [Fact]
    public void PowerShellScript_WhitespaceScript_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "App";
                p.Manufacturer = "Corp";
                p.CustomAction("CA_PS", ca =>
                {
                    ca.PowerShellScript("   ");
                });
            }));
    }

    [Fact]
    public void PowerShellFile_NullPath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "App";
                p.Manufacturer = "Corp";
                p.CustomAction("CA_PS", ca =>
                {
                    ca.PowerShellFile(null!);
                });
            }));
    }

    [Fact]
    public void PowerShellFile_EmptyPath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "App";
                p.Manufacturer = "Corp";
                p.CustomAction("CA_PS", ca =>
                {
                    ca.PowerShellFile("");
                });
            }));
    }

    [Fact]
    public void PowerShellFile_NonExistentFile_ThrowsFileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(() =>
            InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "App";
                p.Manufacturer = "Corp";
                p.CustomAction("CA_PS", ca =>
                {
                    ca.PowerShellFile(@"C:\nonexistent\script.ps1");
                });
            }));
    }

    [Fact]
    public void PowerShellFile_ReadsFileAndCreatesInlineScript()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "Get-Service | Stop-Service");

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "App";
                p.Manufacturer = "Corp";
                p.CustomAction("CA_PS", ca =>
                {
                    ca.PowerShellFile(tempFile);
                });
            });

            var action = package.CustomActions[0];
            Assert.Equal(CustomActionType.ExeInDir, action.Type);
            Assert.Equal("SystemFolder", action.SourceRef);
            Assert.NotNull(action.Target);
            Assert.Contains("Get-Service | Stop-Service", action.Target);
            Assert.Contains("-Command", action.Target);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void PowerShellScript_ChainsWithDeferred()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CustomAction("CA_PS", ca =>
            {
                ca.PowerShellScript("Stop-Service MyService").Deferred().NoImpersonate();
                ca.After = "InstallFiles";
            });
        });

        var action = package.CustomActions[0];
        var expectedType = CustomActionType.ExeInDir
                         | CustomActionType.InScript
                         | CustomActionType.NoImpersonate;
        Assert.Equal(expectedType, action.Type);
        Assert.Equal("InstallFiles", action.After);
    }

    [Fact]
    public void PowerShellScript_IntegrationWithPackageBuilder()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "FullApp";
            p.Manufacturer = "Corp";
            p.CustomAction("CA_PSImmediate", ca =>
            {
                ca.PowerShellScript("Write-Host 'Starting install'");
                ca.Condition = "NOT Installed";
            });
            p.CustomAction("CA_PSDeferred", ca =>
            {
                ca.PowerShellScript("New-Item -Path C:\\Logs -ItemType Directory")
                    .Deferred()
                    .NoImpersonate();
                ca.After = "InstallFiles";
            });
        });

        Assert.Equal(2, package.CustomActions.Count);

        // Immediate action
        var immediate = package.CustomActions[0];
        Assert.Equal(CustomActionType.ExeInDir, immediate.Type);
        Assert.Equal("NOT Installed", immediate.Condition);

        // Deferred action
        var deferred = package.CustomActions[1];
        Assert.Equal(
            CustomActionType.ExeInDir | CustomActionType.InScript | CustomActionType.NoImpersonate,
            deferred.Type);
        Assert.Equal("InstallFiles", deferred.After);

        // Both should validate
        var result = InstallerValidator.Validate(package);
        Assert.DoesNotContain(result.Errors, e => e.Code.StartsWith("CA0"));
    }

    [Fact]
    public void PowerShellScript_WithSpecialCharacters_EscapesCorrectly()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CustomAction("CA_PS", ca =>
            {
                ca.PowerShellScript("$env:PATH = \"C:\\Tools;\" + $env:PATH");
            });
        });

        var target = package.CustomActions[0].Target!;
        // Verify the command line is well-formed with escaped quotes
        Assert.StartsWith("powershell.exe", target);
        Assert.Contains("-Command", target);
        // The inner quotes should be escaped
        Assert.Contains("\\\"C:\\Tools;\\\"", target);
    }
}
