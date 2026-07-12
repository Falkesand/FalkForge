using System;
using System.Collections.Generic;
using System.Linq;
using FalkForge.Models;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Core.Tests;

/// <summary>
/// Fail-loud validation for authored custom dialogs (DLG010–DLG019). An invalid dialog must
/// surface a precise rule error at validation time rather than being silently dropped or
/// producing an opaque MSI foreign-key failure at emit time.
/// </summary>
public sealed class CustomDialogValidationTests
{
    private static PackageModel PackageWith(params CustomDialogModel[] dialogs) => new()
    {
        Name = "App",
        Manufacturer = "FalkForge",
        Version = new Version(1, 0, 0),
        UpgradeCode = Guid.NewGuid(),
        ProductCode = Guid.NewGuid(),
        Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }],
        CustomDialogs = dialogs,
    };

    private static CustomDialogControlModel Button(string name, string? next = null) => new()
    {
        Name = name,
        Type = CustomControlType.PushButton,
        X = 10,
        Y = 10,
        Width = 50,
        Height = 17,
        Text = name,
        NextControl = next,
    };

    [Fact]
    public void Empty_dialog_id_fails_loud_with_DLG010()
    {
        var dialog = new CustomDialogModel { Id = "", Controls = [Button("Ok")] };
        var report = ModelValidator.Inspect(PackageWith(dialog));

        Assert.Contains(report.Errors, e => e.RuleId.Value == "DLG010");
    }

    [Fact]
    public void Dangling_control_next_fails_loud_with_DLG017()
    {
        // "Ok" tab-links to "Missing" which is not a control on the dialog.
        var dialog = new CustomDialogModel { Id = "MyDlg", Controls = [Button("Ok", next: "Missing")] };
        var report = ModelValidator.Inspect(PackageWith(dialog));

        Assert.Contains(report.Errors, e => e.RuleId.Value == "DLG017");
    }

    [Fact]
    public void Property_bound_control_without_property_fails_loud_with_DLG018()
    {
        var edit = new CustomDialogControlModel
        {
            Name = "KeyEdit",
            Type = CustomControlType.Edit,
            X = 10, Y = 10, Width = 100, Height = 18,
            Property = null, // Edit must be bound to a property.
        };
        var dialog = new CustomDialogModel { Id = "MyDlg", Controls = [edit] };
        var report = ModelValidator.Inspect(PackageWith(dialog));

        Assert.Contains(report.Errors, e => e.RuleId.Value == "DLG018");
    }

    [Fact]
    public void Duplicate_control_names_fail_loud_with_DLG016()
    {
        var dialog = new CustomDialogModel { Id = "MyDlg", Controls = [Button("Ok"), Button("Ok")] };
        var report = ModelValidator.Inspect(PackageWith(dialog));

        Assert.Contains(report.Errors, e => e.RuleId.Value == "DLG016");
    }

    [Fact]
    public void Empty_event_verb_fails_loud_with_DLG020()
    {
        // A hand-built model with an empty event verb would otherwise throw an opaque
        // ArgumentException in the translator; DLG020 catches it as a loud validation error.
        var button = new CustomDialogControlModel
        {
            Name = "Ok", Type = CustomControlType.PushButton,
            X = 10, Y = 10, Width = 50, Height = 17, Text = "Ok",
            Events = [new CustomDialogControlEventModel { Event = "", Argument = "Return" }],
        };
        var report = ModelValidator.Inspect(PackageWith(new CustomDialogModel { Id = "MyDlg", Controls = [button] }));

        Assert.Contains(report.Errors, e => e.RuleId.Value == "DLG020");
    }

    [Fact]
    public void NewDialog_event_without_a_target_fails_loud_with_DLG021()
    {
        var button = new CustomDialogControlModel
        {
            Name = "Next", Type = CustomControlType.PushButton,
            X = 10, Y = 10, Width = 50, Height = 17, Text = "Next",
            Events = [new CustomDialogControlEventModel { Event = "NewDialog", Argument = "" }],
        };
        var report = ModelValidator.Inspect(PackageWith(new CustomDialogModel { Id = "MyDlg", Controls = [button] }));

        Assert.Contains(report.Errors, e => e.RuleId.Value == "DLG021");
    }

    [Fact]
    public void VolumeCostList_without_a_property_does_not_trigger_DLG018()
    {
        // The MSI Control table Property column is not used for VolumeCostList, so a missing
        // property is legal and must not be flagged.
        var vcl = new CustomDialogControlModel
        {
            Name = "Costs",
            Type = CustomControlType.VolumeCostList,
            X = 10, Y = 10, Width = 300, Height = 100,
            Property = null,
        };
        var dialog = new CustomDialogModel { Id = "CostDlg", Controls = [vcl] };
        var report = ModelValidator.Inspect(PackageWith(dialog));

        Assert.DoesNotContain(report.Errors, e => e.RuleId.Value == "DLG018");
    }

    [Fact]
    public void Well_formed_custom_dialog_produces_no_DLG_errors()
    {
        var dialog = new CustomDialogModel
        {
            Id = "LicenseKeyDlg",
            Title = "License",
            Controls =
            [
                new CustomDialogControlModel
                {
                    Name = "KeyEdit", Type = CustomControlType.Edit,
                    X = 20, Y = 50, Width = 330, Height = 18, Property = "LICENSEKEY",
                    NextControl = "Next",
                },
                Button("Next"),
            ],
        };
        var report = ModelValidator.Inspect(PackageWith(dialog));

        Assert.DoesNotContain(report.Errors, e => e.RuleId.Value.StartsWith("DLG01", StringComparison.Ordinal));
    }
}
