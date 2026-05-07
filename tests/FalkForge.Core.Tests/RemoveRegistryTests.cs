using FalkForge.Builders;
using FalkForge.Models;
using FalkForge.Testing;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Core.Tests;

public sealed class RemoveRegistryTests
{
    // --- RemoveRegistryBuilder fluent API tests ---

    [Fact]
    public void Builder_Id_SetsId()
    {
        var builder = new RemoveRegistryBuilder();
        builder.Id("RemReg1");
        var model = BuildModel(builder);

        Assert.Equal("RemReg1", model.Id);
    }

    [Fact]
    public void Builder_Root_SetsRoot()
    {
        var builder = new RemoveRegistryBuilder();
        builder.Root(RegistryRoot.CurrentUser);
        var model = BuildModel(builder);

        Assert.Equal(RegistryRoot.CurrentUser, model.Root);
    }

    [Fact]
    public void Builder_Key_SetsKey()
    {
        var builder = new RemoveRegistryBuilder();
        builder.Key(@"SOFTWARE\MyApp");
        var model = BuildModel(builder);

        Assert.Equal(@"SOFTWARE\MyApp", model.Key);
    }

    [Fact]
    public void Builder_Name_SetsName()
    {
        var builder = new RemoveRegistryBuilder();
        builder.Name("SettingValue");
        var model = BuildModel(builder);

        Assert.Equal("SettingValue", model.Name);
    }

    [Fact]
    public void Builder_RemoveKey_SetsActionToRemoveKey()
    {
        var builder = new RemoveRegistryBuilder();
        builder.RemoveKey();
        var model = BuildModel(builder);

        Assert.Equal(RemoveRegistryAction.RemoveKey, model.Action);
    }

    [Fact]
    public void Builder_RemoveValue_SetsActionToRemoveValue()
    {
        var builder = new RemoveRegistryBuilder();
        builder.RemoveValue();
        var model = BuildModel(builder);

        Assert.Equal(RemoveRegistryAction.RemoveValue, model.Action);
    }

    [Fact]
    public void Builder_DefaultAction_IsRemoveKey()
    {
        var builder = new RemoveRegistryBuilder();
        var model = BuildModel(builder);

        Assert.Equal(RemoveRegistryAction.RemoveKey, model.Action);
    }

    [Fact]
    public void Builder_ComponentRef_SetsComponentRef()
    {
        var builder = new RemoveRegistryBuilder();
        builder.ComponentRef("MyComponent");
        var model = BuildModel(builder);

        Assert.Equal("MyComponent", model.ComponentRef);
    }

    [Fact]
    public void Builder_FluentChaining_AllPropertiesSet()
    {
        var builder = new RemoveRegistryBuilder();
        builder
            .Id("RemReg_Full")
            .Root(RegistryRoot.LocalMachine)
            .Key(@"SOFTWARE\OldApp")
            .RemoveKey()
            .ComponentRef("Comp1");

        var model = BuildModel(builder);

        Assert.Equal("RemReg_Full", model.Id);
        Assert.Equal(RegistryRoot.LocalMachine, model.Root);
        Assert.Equal(@"SOFTWARE\OldApp", model.Key);
        Assert.Equal(RemoveRegistryAction.RemoveKey, model.Action);
        Assert.Null(model.Name);
        Assert.Equal("Comp1", model.ComponentRef);
    }

    [Fact]
    public void Builder_RemoveValue_WithName_AllPropertiesSet()
    {
        var builder = new RemoveRegistryBuilder();
        builder
            .Id("RemVal1")
            .Root(RegistryRoot.CurrentUser)
            .Key(@"SOFTWARE\MyApp\Settings")
            .Name("OldSetting")
            .RemoveValue();

        var model = BuildModel(builder);

        Assert.Equal("RemVal1", model.Id);
        Assert.Equal(RegistryRoot.CurrentUser, model.Root);
        Assert.Equal(@"SOFTWARE\MyApp\Settings", model.Key);
        Assert.Equal("OldSetting", model.Name);
        Assert.Equal(RemoveRegistryAction.RemoveValue, model.Action);
    }

    // --- PackageBuilder integration tests ---

    [Fact]
    public void PackageBuilder_RemoveRegistry_AddsEntryToPackage()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.RemoveRegistry(r => r
                .Id("RemReg1")
                .Root(RegistryRoot.LocalMachine)
                .Key(@"SOFTWARE\OldApp")
                .RemoveKey());
        });

        Assert.Single(package.RemoveRegistryEntries);
        Assert.Equal("RemReg1", package.RemoveRegistryEntries[0].Id);
        Assert.Equal(RegistryRoot.LocalMachine, package.RemoveRegistryEntries[0].Root);
        Assert.Equal(@"SOFTWARE\OldApp", package.RemoveRegistryEntries[0].Key);
        Assert.Equal(RemoveRegistryAction.RemoveKey, package.RemoveRegistryEntries[0].Action);
    }

    [Fact]
    public void PackageBuilder_MultipleRemoveRegistry_AllAdded()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.RemoveRegistry(r => r
                .Id("RemReg1")
                .Root(RegistryRoot.LocalMachine)
                .Key(@"SOFTWARE\OldApp")
                .RemoveKey());
            p.RemoveRegistry(r => r
                .Id("RemVal1")
                .Root(RegistryRoot.CurrentUser)
                .Key(@"SOFTWARE\OldApp\Settings")
                .Name("LegacyValue")
                .RemoveValue());
        });

        Assert.Equal(2, package.RemoveRegistryEntries.Count);
        Assert.Equal("RemReg1", package.RemoveRegistryEntries[0].Id);
        Assert.Equal("RemVal1", package.RemoveRegistryEntries[1].Id);
    }

    // --- Validation tests ---

    [Fact]
    public void Validation_MissingId_ProducesRRG001Error()
    {
        var package = BuildPackageWithRemoveRegistry(r => r
            .Root(RegistryRoot.LocalMachine)
            .Key(@"SOFTWARE\OldApp")
            .RemoveKey());

        var result = InstallerValidator.Validate(package);

        Assert.Contains(result.Errors, e => e.RuleId.Value == "RRG001");
    }

    [Fact]
    public void Validation_MissingKey_ProducesRRG002Error()
    {
        var package = BuildPackageWithRemoveRegistry(r => r
            .Id("RemReg1")
            .Root(RegistryRoot.LocalMachine)
            .RemoveKey());

        var result = InstallerValidator.Validate(package);

        Assert.Contains(result.Errors, e => e.RuleId.Value == "RRG002");
    }

    [Fact]
    public void Validation_RemoveValueWithoutName_ProducesRRG003Error()
    {
        var package = BuildPackageWithRemoveRegistry(r => r
            .Id("RemVal1")
            .Root(RegistryRoot.CurrentUser)
            .Key(@"SOFTWARE\MyApp")
            .RemoveValue());

        var result = InstallerValidator.Validate(package);

        Assert.Contains(result.Errors, e => e.RuleId.Value == "RRG003");
    }

    [Fact]
    public void Validation_RemoveKeyWithoutName_DoesNotProduceRRG003()
    {
        var package = BuildPackageWithRemoveRegistry(r => r
            .Id("RemReg1")
            .Root(RegistryRoot.LocalMachine)
            .Key(@"SOFTWARE\OldApp")
            .RemoveKey());

        var result = InstallerValidator.Validate(package);

        Assert.DoesNotContain(result.Errors, e => e.RuleId.Value == "RRG003");
    }

    [Fact]
    public void Validation_ValidRemoveKey_PassesRemoveRegistryValidation()
    {
        var package = BuildPackageWithRemoveRegistry(r => r
            .Id("RemReg1")
            .Root(RegistryRoot.LocalMachine)
            .Key(@"SOFTWARE\OldApp")
            .RemoveKey());

        var result = InstallerValidator.Validate(package);

        Assert.DoesNotContain(result.Errors, e => e.RuleId.Value.StartsWith("RRG"));
    }

    [Fact]
    public void Validation_ValidRemoveValue_PassesRemoveRegistryValidation()
    {
        var package = BuildPackageWithRemoveRegistry(r => r
            .Id("RemVal1")
            .Root(RegistryRoot.CurrentUser)
            .Key(@"SOFTWARE\MyApp\Settings")
            .Name("OldSetting")
            .RemoveValue());

        var result = InstallerValidator.Validate(package);

        Assert.DoesNotContain(result.Errors, e => e.RuleId.Value.StartsWith("RRG"));
    }

    // --- Helpers ---

    private static RemoveRegistryModel BuildModel(RemoveRegistryBuilder builder)
    {
        // Use reflection to invoke internal Build() method
        var buildMethod = typeof(RemoveRegistryBuilder).GetMethod("Build",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return (RemoveRegistryModel)buildMethod!.Invoke(builder, null)!;
    }

    private static PackageModel BuildPackageWithRemoveRegistry(Action<RemoveRegistryBuilder> configure)
    {
        return InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.RemoveRegistry(configure);
        });
    }
}
