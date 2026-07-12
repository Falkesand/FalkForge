using System;
using System.Collections;
using System.Collections.Frozen;
using System.Collections.Generic;
using FalkForge.Extensibility;

namespace FalkForge.Compiler.Msi.UI.Layout;

/// <summary>
/// Mutable registry of dialog step builders. Builders are registered by name during the
/// compilation setup phase and then frozen before the template pipeline begins. After freezing,
/// the registry is read-only and safe to share across threads.
/// </summary>
/// <remarks>
/// The registry tracks two things:
/// <list type="bullet">
/// <item><description>
/// <b>MSI-capable builders</b> — those implementing <see cref="IMsiDialogStepBuilder"/>. Only
/// these can produce an <see cref="MsiDialogModel"/>, so only these are returned by
/// <see cref="TryGet"/> and enumerated for emission.
/// </description></item>
/// <item><description>
/// <b>Known step names</b> — every registered name (MSI-capable or not). DLG001 name-resolution
/// (<see cref="Contains"/>) passes for any registered name; a plain
/// <see cref="IDialogStepBuilder"/> that only carries a name satisfies validation without
/// emitting a dialog. There is no throwing placeholder — an unknown-capable step simply is not
/// enumerated for emission.
/// </description></item>
/// </list>
/// Extension-contributed builders are drained into this registry from
/// <see cref="IExtensionRegistry.RegisterDialogStep"/> during <c>MsiAuthoring.Compile</c>.
/// </remarks>
internal sealed class DialogStepRegistry : IEnumerable<IMsiDialogStepBuilder>
{
    // MSI-capable builders participate in emission and TryGet lookup.
    private readonly Dictionary<string, IMsiDialogStepBuilder> _builders =
        new(StringComparer.Ordinal);

    // Every registered step name (MSI-capable or name-only) for DLG001 resolution.
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);

    private FrozenDictionary<string, IMsiDialogStepBuilder>? _frozenBuilders;
    private FrozenSet<string>? _frozenNames;
    private bool _isFrozen;

    /// <summary>Number of registered MSI-capable step builders (those enumerated for emission).</summary>
    public int Count => _isFrozen ? _frozenBuilders!.Count : _builders.Count;

    /// <summary>
    /// Registers an MSI-capable step builder. Throws if the name is already registered or if the
    /// registry has been frozen.
    /// </summary>
    public void Register(IMsiDialogStepBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        EnsureNotFrozen(builder.Name);

        if (!_names.Add(builder.Name))
        {
            throw new InvalidOperationException(
                $"A dialog step builder with name '{builder.Name}' is already registered.");
        }

        _builders[builder.Name] = builder;
    }

    /// <summary>
    /// Registers an <see cref="IDialogStepBuilder"/> contributed by an extension. Builders that
    /// also implement <see cref="IMsiDialogStepBuilder"/> participate fully in dialog emission;
    /// a plain <see cref="IDialogStepBuilder"/> only reserves its name for DLG001 resolution.
    /// Throws if the name is already registered or if the registry has been frozen.
    /// </summary>
    public void RegisterExtensionBuilder(IDialogStepBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        EnsureNotFrozen(builder.Name);

        if (!_names.Add(builder.Name))
        {
            throw new InvalidOperationException(
                $"A dialog step builder with name '{builder.Name}' is already registered.");
        }

        // Only MSI-capable builders can supply dialog layout; plain builders reserve their name.
        if (builder is IMsiDialogStepBuilder msi)
        {
            _builders[msi.Name] = msi;
        }
    }

    /// <summary>
    /// Freezes the registry. After freezing, <see cref="Register"/> throws and lookups use
    /// frozen collections for O(1) access. Idempotent.
    /// </summary>
    public void Freeze()
    {
        if (_isFrozen)
        {
            return;
        }

        _frozenBuilders = _builders.ToFrozenDictionary(StringComparer.Ordinal);
        _frozenNames = _names.ToFrozenSet(StringComparer.Ordinal);
        _isFrozen = true;
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="name"/> was registered (as an MSI-capable
    /// or a name-only builder). Used by DLG001 to resolve inserted-step names.
    /// </summary>
    public bool Contains(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _isFrozen ? _frozenNames!.Contains(name) : _names.Contains(name);
    }

    /// <summary>
    /// Looks up an MSI-capable step builder by name. Returns <see langword="false"/> for unknown
    /// names and for names registered only as a plain <see cref="IDialogStepBuilder"/>.
    /// </summary>
    public bool TryGet(string name, out IMsiDialogStepBuilder? builder)
    {
        ArgumentNullException.ThrowIfNull(name);

        var lookup = _isFrozen
            ? (IDictionary<string, IMsiDialogStepBuilder>)_frozenBuilders!
            : _builders;

        return lookup.TryGetValue(name, out builder);
    }

    /// <inheritdoc/>
    public IEnumerator<IMsiDialogStepBuilder> GetEnumerator()
    {
        var source = _isFrozen
            ? (IEnumerable<KeyValuePair<string, IMsiDialogStepBuilder>>)_frozenBuilders!
            : _builders;

        foreach (var pair in source)
        {
            yield return pair.Value;
        }
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void EnsureNotFrozen(string name)
    {
        if (_isFrozen)
        {
            throw new InvalidOperationException(
                $"Cannot register step builder '{name}' — the registry has already been frozen.");
        }
    }
}
