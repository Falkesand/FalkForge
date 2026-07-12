using FalkForge.Builders;
using FalkForge.Models;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Core.Tests.Builders;

/// <summary>
/// D1: <c>ShortcutBuilder.OnDesktop()/OnStartMenu()/OnStartup()</c> each emit their shortcut
/// immediately (<c>_onAdd(BuildCurrent())</c>) so the fluent chain can produce one shortcut per
/// requested location without a terminal <c>.Build()</c> call. That immediate-emit design makes
/// a <c>With*</c> call placed AFTER the last <c>On*</c> call in a chain a silent no-op — it
/// configures a shortcut that will never be built. A true reorder (buffer config, apply
/// regardless of call order, emit at finalize) would require a new terminal call the whole
/// public API and every demo lacks today, so this is FAIL-LOUD instead: any <c>With*</c> call
/// after any <c>On*</c> call throws, so the mistake is caught at authoring time rather than
/// silently dropped into the compiled MSI.
/// </summary>
public sealed class ShortcutBuilderTests
{
    [Fact]
    public void WithArguments_AfterOnDesktop_ThrowsInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "App";
                p.Manufacturer = "Corp";
                p.Shortcut("My App", "app.exe").OnDesktop().WithArguments("--x");
            }));

        Assert.Contains("WithArguments", ex.Message);
    }

    [Fact]
    public void WithDescription_AfterOnStartMenu_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() =>
            InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "App";
                p.Manufacturer = "Corp";
                p.Shortcut("My App", "app.exe").OnStartMenu().WithDescription("desc");
            }));
    }

    [Fact]
    public void WithIcon_AfterOnStartup_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() =>
            InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "App";
                p.Manufacturer = "Corp";
                p.Shortcut("My App", "app.exe").OnStartup().WithIcon("app.ico");
            }));
    }

    [Fact]
    public void WithWorkingDirectory_AfterOnDesktop_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() =>
            InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "App";
                p.Manufacturer = "Corp";
                p.Shortcut("My App", "app.exe").OnDesktop().WithWorkingDirectory(@"C:\App");
            }));
    }

    [Fact]
    public void WithArguments_BeforeOnDesktop_AppliesArgumentsNormally()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Shortcut("My App", "app.exe").WithArguments("--x").OnDesktop();
        });

        Assert.Single(package.Shortcuts);
        Assert.Equal("--x", package.Shortcuts[0].Arguments);
    }

    [Fact]
    public void MultipleOnCalls_WithNoInterveningWith_EmitsOneShortcutPerLocation()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Shortcut("My App", "app.exe")
                .WithArguments("--shared")
                .OnDesktop()
                .OnStartMenu("MyCompany")
                .OnStartup();
        });

        Assert.Equal(3, package.Shortcuts.Count);
        Assert.Contains(package.Shortcuts, s => s.Locations.Contains(ShortcutLocation.Desktop));
        Assert.Contains(package.Shortcuts, s => s.Locations.Contains(ShortcutLocation.StartMenu));
        Assert.Contains(package.Shortcuts, s => s.Locations.Contains(ShortcutLocation.Startup));
        Assert.All(package.Shortcuts, s => Assert.Equal("--shared", s.Arguments));

        var startMenuShortcut = Assert.Single(package.Shortcuts, s => s.Locations.Contains(ShortcutLocation.StartMenu));
        Assert.Equal("MyCompany", startMenuShortcut.StartMenuSubfolder);
    }
}
