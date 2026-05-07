using System.Collections.Immutable;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Core.Tests.Models;

public sealed class DialogCustomizationModelTests
{
    [Fact]
    public void Default_construction_yields_all_nulls_and_empty_collections()
    {
        var model = new DialogCustomizationModel();

        Assert.Null(model.BannerBitmap);
        Assert.Null(model.DialogBitmap);
        Assert.Null(model.HeaderIcon);
        Assert.Null(model.WindowTitle);
        Assert.Empty(model.ButtonLabelOverrides);
        Assert.Empty(model.SuppressedDialogs);
    }

    [Fact]
    public void With_expression_updates_banner()
    {
        var model = new DialogCustomizationModel();
        var updated = model with { BannerBitmap = "banner.bmp" };

        Assert.Null(model.BannerBitmap);
        Assert.Equal("banner.bmp", updated.BannerBitmap);
    }

    [Fact]
    public void Frozen_button_overrides_round_trip()
    {
        var overrides = ImmutableDictionary<DialogButton, string>.Empty
            .Add(DialogButton.Next, "Continue")
            .Add(DialogButton.Cancel, "Abort");

        var model = new DialogCustomizationModel { ButtonLabelOverrides = overrides };

        Assert.Equal(2, model.ButtonLabelOverrides.Count);
        Assert.Equal("Continue", model.ButtonLabelOverrides[DialogButton.Next]);
        Assert.Equal("Abort", model.ButtonLabelOverrides[DialogButton.Cancel]);
    }

    [Fact]
    public void Frozen_suppressed_dialogs_round_trip()
    {
        var suppressed = ImmutableHashSet<StockDialog>.Empty
            .Add(StockDialog.License)
            .Add(StockDialog.Maintenance);

        var model = new DialogCustomizationModel { SuppressedDialogs = suppressed };

        Assert.Equal(2, model.SuppressedDialogs.Count);
        Assert.Contains(StockDialog.License, model.SuppressedDialogs);
        Assert.Contains(StockDialog.Maintenance, model.SuppressedDialogs);
    }

    [Fact]
    public void Default_construction_yields_empty_inserted_steps()
    {
        var model = new DialogCustomizationModel();

        Assert.Empty(model.InsertedSteps);
    }

    [Fact]
    public void Inserted_steps_round_trip()
    {
        var steps = ImmutableArray.Create(
            new InsertedDialogStep("StepA", StockDialog.License),
            new InsertedDialogStep("StepB", StockDialog.Welcome));

        var model = new DialogCustomizationModel { InsertedSteps = steps };

        Assert.Equal(2, model.InsertedSteps.Length);
        Assert.Equal("StepA", model.InsertedSteps[0].StepName);
        Assert.Equal(StockDialog.License, model.InsertedSteps[0].After);
        Assert.Equal("StepB", model.InsertedSteps[1].StepName);
    }
}
