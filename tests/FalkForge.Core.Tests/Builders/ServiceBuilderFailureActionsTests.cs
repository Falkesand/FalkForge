using FalkForge;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Core.Tests.Builders;

public sealed class ServiceBuilderFailureActionsTests
{
    [Fact]
    public void FailureActions_SetsAllFieldsOnServiceModel()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Service("MyService", svc =>
            {
                svc.Executable = "svc.exe";
                svc.FailureActions(fa =>
                {
                    fa.OnFirstFailure = FailureAction.Restart;
                    fa.OnSecondFailure = FailureAction.Restart;
                    fa.OnSubsequentFailures = FailureAction.RunCommand;
                    fa.ResetPeriod = TimeSpan.FromDays(2);
                    fa.RestartDelay = TimeSpan.FromSeconds(45);
                    fa.Command = "cmd.exe /c echo hi";
                    fa.RebootMessage = "Rebooting after repeated failures.";
                });
            });
        });

        var failureActions = package.Services[0].FailureActions;
        Assert.NotNull(failureActions);
        Assert.Equal(FailureAction.Restart, failureActions!.OnFirstFailure);
        Assert.Equal(FailureAction.Restart, failureActions.OnSecondFailure);
        Assert.Equal(FailureAction.RunCommand, failureActions.OnSubsequentFailures);
        Assert.Equal(TimeSpan.FromDays(2), failureActions.ResetPeriod);
        Assert.Equal(TimeSpan.FromSeconds(45), failureActions.RestartDelay);
        Assert.Equal("cmd.exe /c echo hi", failureActions.Command);
        Assert.Equal("Rebooting after repeated failures.", failureActions.RebootMessage);
    }

    [Fact]
    public void FailureActions_DefaultsToNull()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Service("MyService", svc =>
            {
                svc.Executable = "svc.exe";
            });
        });

        Assert.Null(package.Services[0].FailureActions);
    }

    [Fact]
    public void FailureActions_UsesModelDefaults_WhenNotConfigured()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Service("MyService", svc =>
            {
                svc.Executable = "svc.exe";
                svc.FailureActions(fa => { });
            });
        });

        var failureActions = package.Services[0].FailureActions;
        Assert.NotNull(failureActions);
        Assert.Equal(FailureAction.None, failureActions!.OnFirstFailure);
        Assert.Equal(FailureAction.None, failureActions.OnSecondFailure);
        Assert.Equal(FailureAction.None, failureActions.OnSubsequentFailures);
        Assert.Equal(TimeSpan.FromDays(1), failureActions.ResetPeriod);
        Assert.Equal(TimeSpan.FromMinutes(1), failureActions.RestartDelay);
        Assert.Null(failureActions.Command);
        Assert.Null(failureActions.RebootMessage);
    }
}
