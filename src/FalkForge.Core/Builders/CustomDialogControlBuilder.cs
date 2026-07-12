using System;
using System.Collections.Generic;

using FalkForge.Models;

namespace FalkForge.Builders;

/// <summary>
/// Fluent builder for a single control on a <see cref="CustomDialogBuilder"/>. Configures the
/// control's text, bound property, tab order, raw attribute bits, and the control events and
/// conditions it contributes. Obtained through the control-adder methods on
/// <see cref="CustomDialogBuilder"/> (for example <see cref="CustomDialogBuilder.PushButton"/>).
/// </summary>
public sealed class CustomDialogControlBuilder
{
    // Control table attribute bits — see MSI Control Table documentation.
    private const int AttrVisible = 0x0001;
    private const int AttrEnabled = 0x0002;
    private const int AttrSunken = 0x0004;
    private const int AttrRightAligned = 0x0040;
    private const int AttrTransparent = 0x00010000;
    private const int AttrNoPrefix = 0x00020000;

    private readonly string _name;
    private readonly CustomControlType _type;
    private readonly int _x;
    private readonly int _y;
    private readonly int _width;
    private readonly int _height;
    private readonly List<CustomDialogControlEventModel> _events = [];
    private readonly List<CustomDialogControlConditionModel> _conditions = [];

    private int _attributes = AttrVisible | AttrEnabled;
    private string? _text;
    private string? _property;
    private string? _next;

    internal CustomDialogControlBuilder(
        string name, CustomControlType type, int x, int y, int width, int height, string? text, string? property)
    {
        _name = name;
        _type = type;
        _x = x;
        _y = y;
        _width = width;
        _height = height;
        _text = text;
        _property = property;
    }

    /// <summary>Sets the control text (or, for bitmap/icon controls, the embedded <c>Binary</c> stream name).</summary>
    public CustomDialogControlBuilder Text(string text)
    {
        _text = text;
        return this;
    }

    /// <summary>Binds the control to an MSI property.</summary>
    public CustomDialogControlBuilder Property(string property)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(property);
        _property = property;
        return this;
    }

    /// <summary>Sets the control that receives focus next in tab order (maps to <c>Control_Next</c>).</summary>
    public CustomDialogControlBuilder Next(string controlName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlName);
        _next = controlName;
        return this;
    }

    /// <summary>Replaces the raw <c>Control</c> attribute bitmask outright.</summary>
    public CustomDialogControlBuilder Attributes(int attributes)
    {
        _attributes = attributes;
        return this;
    }

    /// <summary>Makes the control initially hidden (clears the Visible bit).</summary>
    public CustomDialogControlBuilder Hidden()
    {
        _attributes &= ~AttrVisible;
        return this;
    }

    /// <summary>Makes the control initially disabled (clears the Enabled bit).</summary>
    public CustomDialogControlBuilder Disabled()
    {
        _attributes &= ~AttrEnabled;
        return this;
    }

    /// <summary>Gives the control a sunken 3-D border.</summary>
    public CustomDialogControlBuilder Sunken()
    {
        _attributes |= AttrSunken;
        return this;
    }

    /// <summary>Draws the control with a transparent background (text/bitmap controls).</summary>
    public CustomDialogControlBuilder Transparent()
    {
        _attributes |= AttrTransparent;
        return this;
    }

    /// <summary>Disables <c>&amp;</c> accelerator-prefix processing in the control text.</summary>
    public CustomDialogControlBuilder NoPrefix()
    {
        _attributes |= AttrNoPrefix;
        return this;
    }

    /// <summary>Right-aligns the control text.</summary>
    public CustomDialogControlBuilder RightAligned()
    {
        _attributes |= AttrRightAligned;
        return this;
    }

    // ── Control events ─────────────────────────────────────────────────────────

    /// <summary>Navigates to another dialog in place (a <c>NewDialog</c> event).</summary>
    public CustomDialogControlBuilder NavigateTo(string dialogId, string? condition = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dialogId);
        return PublishEvent("NewDialog", dialogId, condition);
    }

    /// <summary>Opens a modal child dialog (a <c>SpawnDialog</c> event).</summary>
    public CustomDialogControlBuilder SpawnDialog(string dialogId, string? condition = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dialogId);
        return PublishEvent("SpawnDialog", dialogId, condition);
    }

    /// <summary>
    /// Ends the dialog with the given exit code (an <c>EndDialog</c> event). Valid arguments:
    /// <c>Return</c>, <c>Exit</c>, <c>Retry</c>, <c>Ignore</c>.
    /// </summary>
    public CustomDialogControlBuilder EndDialog(string exitCode = "Return", string? condition = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exitCode);
        return PublishEvent("EndDialog", exitCode, condition);
    }

    /// <summary>Runs a custom action (a <c>DoAction</c> event).</summary>
    public CustomDialogControlBuilder DoAction(string actionName, string? condition = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionName);
        return PublishEvent("DoAction", actionName, condition);
    }

    /// <summary>Assigns a value to an MSI property (a <c>[Property]</c> event).</summary>
    public CustomDialogControlBuilder SetProperty(string property, string value, string? condition = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(property);
        return PublishEvent($"[{property}]", value, condition);
    }

    /// <summary>Resets all controls on the dialog to their defaults (a <c>Reset</c> event).</summary>
    public CustomDialogControlBuilder Reset(string? condition = null)
        => PublishEvent("Reset", "0", condition);

    /// <summary>
    /// Adds an arbitrary <c>ControlEvent</c> row. Use the named helpers
    /// (<see cref="NavigateTo"/>, <see cref="EndDialog"/>, …) for the common verbs.
    /// </summary>
    public CustomDialogControlBuilder PublishEvent(
        string @event, string argument, string? condition = null, int ordering = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(@event);
        ArgumentNullException.ThrowIfNull(argument);
        _events.Add(new CustomDialogControlEventModel
        {
            Event = @event,
            Argument = argument,
            Condition = condition,
            Ordering = ordering,
        });
        return this;
    }

    // ── Control conditions ─────────────────────────────────────────────────────

    /// <summary>Shows the control when <paramref name="condition"/> is true.</summary>
    public CustomDialogControlBuilder ShowWhen(string condition)
        => When(CustomConditionAction.Show, condition);

    /// <summary>Hides the control when <paramref name="condition"/> is true.</summary>
    public CustomDialogControlBuilder HideWhen(string condition)
        => When(CustomConditionAction.Hide, condition);

    /// <summary>Enables the control when <paramref name="condition"/> is true.</summary>
    public CustomDialogControlBuilder EnableWhen(string condition)
        => When(CustomConditionAction.Enable, condition);

    /// <summary>Disables the control when <paramref name="condition"/> is true.</summary>
    public CustomDialogControlBuilder DisableWhen(string condition)
        => When(CustomConditionAction.Disable, condition);

    /// <summary>Restores the control's default state when <paramref name="condition"/> is true.</summary>
    public CustomDialogControlBuilder DefaultWhen(string condition)
        => When(CustomConditionAction.Default, condition);

    /// <summary>Adds a <c>ControlCondition</c> row applying <paramref name="action"/> when the condition is true.</summary>
    public CustomDialogControlBuilder When(CustomConditionAction action, string condition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(condition);
        _conditions.Add(new CustomDialogControlConditionModel
        {
            Action = action,
            Condition = condition,
        });
        return this;
    }

    internal CustomDialogControlModel Build() => new()
    {
        Name = _name,
        Type = _type,
        X = _x,
        Y = _y,
        Width = _width,
        Height = _height,
        Attributes = _attributes,
        Property = _property,
        Text = _text,
        NextControl = _next,
        Events = _events.ToArray(),
        Conditions = _conditions.ToArray(),
    };
}
