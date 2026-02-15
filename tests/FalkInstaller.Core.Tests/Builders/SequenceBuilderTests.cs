using FalkInstaller.Builders;
using FalkInstaller.Models;
using FalkInstaller.Testing;
using Xunit;

namespace FalkInstaller.Core.Tests.Builders;

public sealed class SequenceBuilderTests
{
    [Fact]
    public void After_SetsAfterActionPosition()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.ExecuteSequence(s => s
                .Action("MyCustomAction")
                .After("InstallFiles"));
        });

        Assert.Single(package.ExecuteSequenceActions);
        var action = package.ExecuteSequenceActions[0];
        Assert.Equal("MyCustomAction", action.ActionName);
        Assert.IsType<ActionPosition.AfterAction>(action.Position);
        var afterPos = (ActionPosition.AfterAction)action.Position;
        Assert.Equal("InstallFiles", afterPos.ReferenceAction);
    }

    [Fact]
    public void Before_SetsBeforeActionPosition()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.ExecuteSequence(s => s
                .Action("MyCustomAction")
                .Before("InstallFinalize"));
        });

        Assert.Single(package.ExecuteSequenceActions);
        var action = package.ExecuteSequenceActions[0];
        Assert.IsType<ActionPosition.BeforeAction>(action.Position);
        var beforePos = (ActionPosition.BeforeAction)action.Position;
        Assert.Equal("InstallFinalize", beforePos.ReferenceAction);
    }

    [Fact]
    public void At_SetsExplicitSequenceNumber()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.ExecuteSequence(s => s
                .Action("MyCustomAction")
                .At(5500));
        });

        Assert.Single(package.ExecuteSequenceActions);
        var action = package.ExecuteSequenceActions[0];
        Assert.IsType<ActionPosition.AtNumber>(action.Position);
        var atPos = (ActionPosition.AtNumber)action.Position;
        Assert.Equal(5500, atPos.SequenceNumber);
    }

    [Fact]
    public void Condition_SetsConditionString()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.ExecuteSequence(s => s
                .Action("MyCustomAction")
                .After("InstallFiles")
                .Condition("NOT Installed"));
        });

        Assert.Single(package.ExecuteSequenceActions);
        Assert.Equal("NOT Installed", package.ExecuteSequenceActions[0].Condition);
    }

    [Fact]
    public void FluentChaining_MultipleActions_AllRegistered()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.ExecuteSequence(s => s
                .Action("CA_First")
                .After("InstallFiles")
                .Action("CA_Second")
                .Before("InstallFinalize")
                .Action("CA_Third")
                .At(5000));
        });

        Assert.Equal(3, package.ExecuteSequenceActions.Count);
        Assert.Equal("CA_First", package.ExecuteSequenceActions[0].ActionName);
        Assert.Equal("CA_Second", package.ExecuteSequenceActions[1].ActionName);
        Assert.Equal("CA_Third", package.ExecuteSequenceActions[2].ActionName);
    }

    [Fact]
    public void UISequence_SetsCorrectTable()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.UISequence(s => s
                .Action("MyUIAction")
                .After("CostFinalize"));
        });

        Assert.Single(package.UISequenceActions);
        Assert.Equal(SequenceTable.InstallUISequence, package.UISequenceActions[0].Table);
    }

    [Fact]
    public void ExecuteSequence_SetsCorrectTable()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.ExecuteSequence(s => s
                .Action("MyExecAction")
                .After("InstallFiles"));
        });

        Assert.Single(package.ExecuteSequenceActions);
        Assert.Equal(SequenceTable.InstallExecuteSequence, package.ExecuteSequenceActions[0].Table);
    }

    [Fact]
    public void MissingPosition_DefaultsToAt4001()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.ExecuteSequence(s => s.Action("MyAction"));
        });

        Assert.Single(package.ExecuteSequenceActions);
        var pos = package.ExecuteSequenceActions[0].Position;
        Assert.IsType<ActionPosition.AtNumber>(pos);
        Assert.Equal(4001, ((ActionPosition.AtNumber)pos).SequenceNumber);
    }

    [Fact]
    public void NullCondition_WhenNotSet()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.ExecuteSequence(s => s
                .Action("MyAction")
                .After("InstallFiles"));
        });

        Assert.Null(package.ExecuteSequenceActions[0].Condition);
    }

    [Fact]
    public void MultipleCallsToExecuteSequence_Accumulate()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.ExecuteSequence(s => s
                .Action("CA_First")
                .After("InstallFiles"));
            p.ExecuteSequence(s => s
                .Action("CA_Second")
                .Before("InstallFinalize"));
        });

        Assert.Equal(2, package.ExecuteSequenceActions.Count);
        Assert.Equal("CA_First", package.ExecuteSequenceActions[0].ActionName);
        Assert.Equal("CA_Second", package.ExecuteSequenceActions[1].ActionName);
    }
}
