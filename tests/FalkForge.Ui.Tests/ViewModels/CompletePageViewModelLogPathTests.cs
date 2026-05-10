namespace FalkForge.Ui.Tests.ViewModels;

using System;
using System.IO;
using System.Linq;
using FalkForge.Ui.ViewModels;
using Xunit;

public class CompletePageViewModelLogPathTests
{
    private static CompletePageViewModel Create(TestInstallerEngine engine)
    {
        var shell = new DefaultShellViewModel(engine);
        return shell.Pages.OfType<CompletePageViewModel>().Single();
    }

    [Fact]
    public void HasLogPath_IsFalse_WhenLogPathNull()
    {
        var engine = new TestInstallerEngine { LogPath = null };
        var vm = Create(engine);

        Assert.False(vm.HasLogPath);
        Assert.Null(vm.LogPath);
    }

    [Fact]
    public void HasLogPath_IsTrue_WhenLogPathProvided()
    {
        var engine = new TestInstallerEngine { LogPath = @"C:\Temp\session.log" };
        var vm = Create(engine);

        Assert.True(vm.HasLogPath);
        Assert.Equal(@"C:\Temp\session.log", vm.LogPath);
    }

    [Fact]
    public void OpenLogCommand_CannotExecute_WhenLogPathNullOrEmpty()
    {
        var engine = new TestInstallerEngine { LogPath = null };
        var vm = Create(engine);

        Assert.False(vm.OpenLogCommand.CanExecute(null));

        engine = new TestInstallerEngine { LogPath = string.Empty };
        vm = Create(engine);
        Assert.False(vm.OpenLogCommand.CanExecute(null));
    }

    [Fact]
    public void OpenLogCommand_CannotExecute_WhenFileMissing()
    {
        var engine = new TestInstallerEngine { LogPath = @"C:\Does\Not\Exist\nope.log" };
        var vm = Create(engine);

        Assert.False(vm.OpenLogCommand.CanExecute(null));
    }

    [Fact]
    public void OpenLogCommand_CanExecute_WhenFileExists()
    {
        var path = Path.Combine(Path.GetTempPath(), $"falk-vmtest-{Guid.NewGuid():N}.log");
        File.WriteAllText(path, "hello");
        try
        {
            var engine = new TestInstallerEngine { LogPath = path };
            var vm = Create(engine);

            Assert.True(vm.OpenLogCommand.CanExecute(null));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OpenLogFolderCommand_CannotExecute_WhenLogPathNull()
    {
        var engine = new TestInstallerEngine { LogPath = null };
        var vm = Create(engine);

        Assert.False(vm.OpenLogFolderCommand.CanExecute(null));
    }

    [Fact]
    public void OpenLogFolderCommand_CanExecute_WhenFileExists()
    {
        var path = Path.Combine(Path.GetTempPath(), $"falk-vmtest-{Guid.NewGuid():N}.log");
        File.WriteAllText(path, "hello");
        try
        {
            var engine = new TestInstallerEngine { LogPath = path };
            var vm = Create(engine);

            Assert.True(vm.OpenLogFolderCommand.CanExecute(null));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
