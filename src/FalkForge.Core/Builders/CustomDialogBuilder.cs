using System;
using System.Collections.Generic;

using FalkForge.Models;

namespace FalkForge.Builders;

/// <summary>
/// Fluent builder for a custom MSI dialog. Configures the dialog's size, title, attributes,
/// focus/default/cancel controls, and install-UI sequence placement, and adds controls through
/// the typed control-adder methods. Obtained through
/// <see cref="PackageBuilder.AddCustomDialog(string, Action{CustomDialogBuilder})"/>.
/// </summary>
/// <remarks>
/// Each control-adder returns this builder for chaining and accepts an optional
/// <see cref="Action{T}"/> that configures the freshly-added control (text, bound property,
/// tab order, events, conditions) via a <see cref="CustomDialogControlBuilder"/>.
/// </remarks>
public sealed class CustomDialogBuilder
{
    private readonly string _id;
    private readonly List<CustomDialogControlBuilder> _controls = [];

    private string? _title;
    private int _width = 370;
    private int _height = 270;
    private int _hCentering = 50;
    private int _vCentering = 50;
    private int _attributes = 39; // Visible | Modal | Minimize | TrackDiskSpace
    private string? _firstControl;
    private string? _defaultControl;
    private string? _cancelControl;
    private int? _sequenceNumber;

    internal CustomDialogBuilder(string id) => _id = id;

    /// <summary>Sets the dialog window title.</summary>
    public CustomDialogBuilder Title(string title)
    {
        _title = title;
        return this;
    }

    /// <summary>Sets the dialog width and height in dialog units.</summary>
    public CustomDialogBuilder Size(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        _width = width;
        _height = height;
        return this;
    }

    /// <summary>Sets the horizontal and vertical centering percentages (0–100).</summary>
    public CustomDialogBuilder Centering(int horizontal, int vertical)
    {
        _hCentering = horizontal;
        _vCentering = vertical;
        return this;
    }

    /// <summary>Replaces the raw <c>Dialog</c> attribute bitmask outright.</summary>
    public CustomDialogBuilder Attributes(int attributes)
    {
        _attributes = attributes;
        return this;
    }

    /// <summary>Sets the control that receives focus first (maps to <c>Control_First</c>).</summary>
    public CustomDialogBuilder FirstControl(string controlName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlName);
        _firstControl = controlName;
        return this;
    }

    /// <summary>Sets the default (Enter) control (maps to <c>Control_Default</c>).</summary>
    public CustomDialogBuilder DefaultControl(string controlName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlName);
        _defaultControl = controlName;
        return this;
    }

    /// <summary>Sets the cancel (Escape) control (maps to <c>Control_Cancel</c>).</summary>
    public CustomDialogBuilder CancelControl(string controlName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlName);
        _cancelControl = controlName;
        return this;
    }

    /// <summary>
    /// Places the dialog in the <c>InstallUISequence</c> table at <paramref name="sequenceNumber"/>
    /// so it shows as an install-UI screen. The standard first-dialog slot is 1100.
    /// </summary>
    public CustomDialogBuilder Sequence(int sequenceNumber)
    {
        _sequenceNumber = sequenceNumber;
        return this;
    }

    // ── Typed control adders ─────────────────────────────────────────────────────

    /// <summary>Adds a static text control.</summary>
    public CustomDialogBuilder Text(
        string name, int x, int y, int width, int height, string text,
        Action<CustomDialogControlBuilder>? configure = null)
        => Add(CustomControlType.Text, name, x, y, width, height, text, property: null, configure);

    /// <summary>Adds a push button. Wire navigation with control events in <paramref name="configure"/>.</summary>
    public CustomDialogBuilder PushButton(
        string name, int x, int y, int width, int height, string text,
        Action<CustomDialogControlBuilder>? configure = null)
        => Add(CustomControlType.PushButton, name, x, y, width, height, text, property: null, configure);

    /// <summary>Adds a horizontal etched line separator (height is fixed at 0).</summary>
    public CustomDialogBuilder Line(
        string name, int x, int y, int width,
        Action<CustomDialogControlBuilder>? configure = null)
        => Add(CustomControlType.Line, name, x, y, width, 0, text: null, property: null, configure);

    /// <summary>Adds a check box bound to <paramref name="property"/>.</summary>
    public CustomDialogBuilder CheckBox(
        string name, int x, int y, int width, int height, string property, string text,
        Action<CustomDialogControlBuilder>? configure = null)
        => Add(CustomControlType.CheckBox, name, x, y, width, height, text, property, configure);

    /// <summary>Adds a single-line editable text field bound to <paramref name="property"/>.</summary>
    public CustomDialogBuilder Edit(
        string name, int x, int y, int width, int height, string property,
        Action<CustomDialogControlBuilder>? configure = null)
        => Add(CustomControlType.Edit, name, x, y, width, height, text: null, property, configure);

    /// <summary>Adds an editable path field bound to a directory property.</summary>
    public CustomDialogBuilder PathEdit(
        string name, int x, int y, int width, int height, string property,
        Action<CustomDialogControlBuilder>? configure = null)
        => Add(CustomControlType.PathEdit, name, x, y, width, height, text: null, property, configure);

    /// <summary>Adds a scrollable read-only multi-line text area (for example a licence).</summary>
    public CustomDialogBuilder ScrollableText(
        string name, int x, int y, int width, int height, string text,
        Action<CustomDialogControlBuilder>? configure = null)
        => Add(CustomControlType.ScrollableText, name, x, y, width, height, text, property: null, configure);

    /// <summary>Adds a bitmap image control referencing an embedded <c>Binary</c> stream by name.</summary>
    public CustomDialogBuilder Bitmap(
        string name, int x, int y, int width, int height, string binaryKey,
        Action<CustomDialogControlBuilder>? configure = null)
        => Add(CustomControlType.Bitmap, name, x, y, width, height, binaryKey, property: null, configure);

    /// <summary>Adds an icon control referencing an embedded <c>Binary</c> stream by name.</summary>
    public CustomDialogBuilder Icon(
        string name, int x, int y, int width, int height, string binaryKey,
        Action<CustomDialogControlBuilder>? configure = null)
        => Add(CustomControlType.Icon, name, x, y, width, height, binaryKey, property: null, configure);

    /// <summary>Adds a labelled group box frame.</summary>
    public CustomDialogBuilder GroupBox(
        string name, int x, int y, int width, int height, string text,
        Action<CustomDialogControlBuilder>? configure = null)
        => Add(CustomControlType.GroupBox, name, x, y, width, height, text, property: null, configure);

    /// <summary>
    /// Adds a control of any <see cref="CustomControlType"/>. Use this escape hatch for control
    /// types without a dedicated adder (for example <see cref="CustomControlType.RadioButtonGroup"/>);
    /// remember such controls may need a companion table for their options.
    /// </summary>
    public CustomDialogBuilder Control(
        CustomControlType type, string name, int x, int y, int width, int height,
        Action<CustomDialogControlBuilder>? configure = null)
        => Add(type, name, x, y, width, height, text: null, property: null, configure);

    private CustomDialogBuilder Add(
        CustomControlType type, string name, int x, int y, int width, int height,
        string? text, string? property, Action<CustomDialogControlBuilder>? configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var control = new CustomDialogControlBuilder(name, type, x, y, width, height, text, property);
        configure?.Invoke(control);
        _controls.Add(control);
        return this;
    }

    internal CustomDialogModel Build()
    {
        var controls = new List<CustomDialogControlModel>(_controls.Count);
        foreach (CustomDialogControlBuilder control in _controls)
        {
            controls.Add(control.Build());
        }

        return new CustomDialogModel
        {
            Id = _id,
            Title = _title,
            Width = _width,
            Height = _height,
            HCentering = _hCentering,
            VCentering = _vCentering,
            Attributes = _attributes,
            FirstControl = _firstControl,
            DefaultControl = _defaultControl,
            CancelControl = _cancelControl,
            SequenceNumber = _sequenceNumber,
            Controls = controls,
        };
    }
}
