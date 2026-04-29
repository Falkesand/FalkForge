using System.Collections.Immutable;
using System.Linq;
using FalkForge.Compiler.Msi.UI;
using FalkForge.Compiler.Msi.UI.Layout;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout;

public sealed class DialogComposerEventsTests
{
    private static DialogContent ContentWith(
        ImmutableArray<DialogControlEvent> events = default,
        ImmutableArray<DialogControlCondition> conditions = default,
        ImmutableArray<DialogEventMapping> mappings = default) => new()
    {
        Name = "WelcomeDlg",
        Kind = "Welcome",
        Placements = ImmutableArray<RegionPlacement>.Empty,
        Events = events.IsDefault ? ImmutableArray<DialogControlEvent>.Empty : events,
        Conditions = conditions.IsDefault ? ImmutableArray<DialogControlCondition>.Empty : conditions,
        EventMappings = mappings.IsDefault ? ImmutableArray<DialogEventMapping>.Empty : mappings,
    };

    [Fact]
    public void Compose_with_no_events_produces_model_with_empty_events()
    {
        var content = ContentWith();

        var model = DialogComposer.Compose(content, Layouts.Standard370x270);

        Assert.Empty(model.Events);
        Assert.Empty(model.Conditions);
        Assert.Empty(model.EventMappings);
    }

    [Fact]
    public void Compose_with_two_events_produces_model_with_two_events_in_order()
    {
        var content = ContentWith(events: ImmutableArray.Create(
            new DialogControlEvent { Control = "NextButton", Event = "EndDialog", Argument = "Return" },
            new DialogControlEvent { Control = "CancelButton", Event = "SpawnDialog", Argument = "CancelDlg" }));

        var model = DialogComposer.Compose(content, Layouts.Standard370x270);

        Assert.Equal(2, model.Events.Count);
        Assert.Equal("NextButton", model.Events[0].ControlName);
        Assert.Equal("EndDialog", model.Events[0].Event.Value);
        Assert.Equal("Return", model.Events[0].Argument);
        Assert.Equal("CancelButton", model.Events[1].ControlName);
        Assert.Equal("SpawnDialog", model.Events[1].Event.Value);
        Assert.Equal("CancelDlg", model.Events[1].Argument);
    }

    [Fact]
    public void Compose_event_with_null_condition_uses_default_one_string()
    {
        var content = ContentWith(events: ImmutableArray.Create(
            new DialogControlEvent { Control = "NextButton", Event = "EndDialog", Argument = "Return" }));

        var model = DialogComposer.Compose(content, Layouts.Standard370x270);

        Assert.Equal("1", model.Events[0].Condition);
    }

    [Fact]
    public void Compose_event_with_explicit_condition_preserves_it()
    {
        var content = ContentWith(events: ImmutableArray.Create(
            new DialogControlEvent
            {
                Control = "NextButton",
                Event = "EndDialog",
                Argument = "Return",
                Condition = "INSTALLED",
            }));

        var model = DialogComposer.Compose(content, Layouts.Standard370x270);

        Assert.Equal("INSTALLED", model.Events[0].Condition);
    }

    [Fact]
    public void Compose_event_propagates_dialog_name()
    {
        var content = ContentWith(events: ImmutableArray.Create(
            new DialogControlEvent { Control = "NextButton", Event = "EndDialog", Argument = "Return" }));

        var model = DialogComposer.Compose(content, Layouts.Standard370x270);

        Assert.Equal("WelcomeDlg", model.Events[0].DialogName);
    }

    [Fact]
    public void Compose_event_set_property_event_round_trips()
    {
        var content = ContentWith(events: ImmutableArray.Create(
            new DialogControlEvent
            {
                Control = "BrowseButton",
                Event = "[INSTALLDIR]",
                Argument = "[_BrowseProperty]",
            }));

        var model = DialogComposer.Compose(content, Layouts.Standard370x270);

        Assert.Equal("[INSTALLDIR]", model.Events[0].Event.Value);
    }

    [Fact]
    public void Compose_with_one_condition_produces_model_with_condition()
    {
        var content = ContentWith(conditions: ImmutableArray.Create(
            new DialogControlCondition
            {
                Control = "NextButton",
                Action = "Disable",
                Condition = "NOT INSTALLDIR",
            }));

        var model = DialogComposer.Compose(content, Layouts.Standard370x270);

        var cond = Assert.Single(model.Conditions);
        Assert.Equal("WelcomeDlg", cond.DialogName);
        Assert.Equal("NextButton", cond.ControlName);
        Assert.Equal(MsiConditionAction.Disable, cond.Action);
        Assert.Equal("NOT INSTALLDIR", cond.Condition);
    }

    [Fact]
    public void Compose_with_one_event_mapping_produces_model_with_mapping()
    {
        var content = ContentWith(mappings: ImmutableArray.Create(
            new DialogEventMapping
            {
                Control = "ProgressBar",
                Event = "SetProgress",
                Attribute = "Progress",
            }));

        var model = DialogComposer.Compose(content, Layouts.Standard370x270);

        var mapping = Assert.Single(model.EventMappings);
        Assert.Equal("WelcomeDlg", mapping.DialogName);
        Assert.Equal("ProgressBar", mapping.ControlName);
        Assert.Equal("SetProgress", mapping.Event);
        Assert.Equal("Progress", mapping.Attribute);
    }

    [Fact]
    public void Compose_event_order_preserved()
    {
        var content = ContentWith(events: ImmutableArray.Create(
            new DialogControlEvent { Control = "NextButton", Event = "DoAction", Argument = "First", Order = 1 },
            new DialogControlEvent { Control = "NextButton", Event = "DoAction", Argument = "Second", Order = 2 },
            new DialogControlEvent { Control = "NextButton", Event = "EndDialog", Argument = "Return", Order = 3 }));

        var model = DialogComposer.Compose(content, Layouts.Standard370x270);

        var orders = model.Events.Select(e => e.Ordering).ToArray();
        Assert.Equal(new[] { 1, 2, 3 }, orders);
        Assert.Equal("First", model.Events[0].Argument);
        Assert.Equal("Second", model.Events[1].Argument);
        Assert.Equal("Return", model.Events[2].Argument);
    }
}
