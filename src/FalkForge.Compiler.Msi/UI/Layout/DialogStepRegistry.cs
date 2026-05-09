using System;
using System.Collections;
using System.Collections.Frozen;
using System.Collections.Generic;
using FalkForge.Extensibility;

namespace FalkForge.Compiler.Msi.UI.Layout;

/// <summary>
/// Mutable registry of <see cref="IMsiDialogStepBuilder"/> instances. Builders are registered
/// by name during the compilation setup phase and then frozen before the template pipeline
/// begins. After freezing, the registry is read-only and safe to share across threads.
/// </summary>
/// <remarks>
/// RFC Cycle 6, step 16. DLG001 validation uses the frozen registry to check that every
/// step name referenced by <see cref="FalkForge.Models.DialogCustomizationModel.InsertedSteps"/>
/// has a matching registered builder.
/// Extension-contributed builders are drained into this registry from
/// <see cref="IExtensionRegistry.RegisterDialogStep"/> during <c>MsiAuthoring.Compile</c>.
/// Only builders that also implement <see cref="IMsiDialogStepBuilder"/> participate in
/// template composition; plain <see cref="IDialogStepBuilder"/> instances satisfy DLG001
/// name resolution without emitting a dialog.
/// </remarks>
internal sealed class DialogStepRegistry : IEnumerable<IMsiDialogStepBuilder>
{
    // Use Dictionary during registration phase; frozen on Freeze().
    private readonly Dictionary<string, IMsiDialogStepBuilder> _builders =
        new(StringComparer.Ordinal);

    private FrozenDictionary<string, IMsiDialogStepBuilder>? _frozen;
    private bool _isFrozen;

    /// <summary>Number of registered step builders.</summary>
    public int Count => _isFrozen ? _frozen!.Count : _builders.Count;

    /// <summary>
    /// Registers a step builder. Throws if the name is already registered or if the
    /// registry has been frozen.
    /// </summary>
    public void Register(IMsiDialogStepBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (_isFrozen)
        {
            throw new InvalidOperationException(
                $"Cannot register step builder '{builder.Name}' — the registry has already been frozen.");
        }

        if (_builders.ContainsKey(builder.Name))
        {
            throw new InvalidOperationException(
                $"A dialog step builder with name '{builder.Name}' is already registered.");
        }

        _builders[builder.Name] = builder;
    }

    /// <summary>
    /// Attempts to register an <see cref="IDialogStepBuilder"/> from an extension.
    /// Only builders that also implement <see cref="IMsiDialogStepBuilder"/> are added;
    /// plain <see cref="IDialogStepBuilder"/> instances are accepted for DLG001 name
    /// resolution by registering a name-only wrapper.
    /// Throws if the name is already registered or if the registry has been frozen.
    /// </summary>
    public void RegisterExtensionBuilder(IDialogStepBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (_isFrozen)
        {
            throw new InvalidOperationException(
                $"Cannot register extension step builder '{builder.Name}' — the registry has already been frozen.");
        }

        if (_builders.ContainsKey(builder.Name))
        {
            throw new InvalidOperationException(
                $"A dialog step builder with name '{builder.Name}' is already registered.");
        }

        // Extension builders that implement IMsiDialogStepBuilder participate fully
        // in dialog composition. Plain IDialogStepBuilder instances satisfy DLG001
        // name resolution only — a name-only wrapper covers that case.
        IMsiDialogStepBuilder msiBuilder = builder is IMsiDialogStepBuilder msi
            ? msi
            : new NameOnlyMsiDialogStepBuilder(builder.Name);

        _builders[msiBuilder.Name] = msiBuilder;
    }

    /// <summary>
    /// Freezes the registry. After freezing, <see cref="Register"/> throws and
    /// <see cref="TryGet"/> uses a <see cref="FrozenDictionary{TKey,TValue}"/> for O(1) lookup.
    /// Idempotent — calling Freeze on an already-frozen registry is a no-op.
    /// </summary>
    public void Freeze()
    {
        if (_isFrozen)
        {
            return;
        }

        _frozen = _builders.ToFrozenDictionary(StringComparer.Ordinal);
        _isFrozen = true;
    }

    /// <summary>
    /// Looks up a step builder by name. Returns <c>true</c> and sets
    /// <paramref name="builder"/> if found; returns <c>false</c> and sets
    /// <paramref name="builder"/> to <c>null</c> if not found.
    /// </summary>
    public bool TryGet(string name, out IMsiDialogStepBuilder? builder)
    {
        ArgumentNullException.ThrowIfNull(name);

        var lookup = _isFrozen
            ? (IDictionary<string, IMsiDialogStepBuilder>)_frozen!
            : _builders;

        return lookup.TryGetValue(name, out builder);
    }

    /// <inheritdoc/>
    public IEnumerator<IMsiDialogStepBuilder> GetEnumerator()
    {
        var source = _isFrozen
            ? (IEnumerable<KeyValuePair<string, IMsiDialogStepBuilder>>)_frozen!
            : _builders;

        foreach (var pair in source)
        {
            yield return pair.Value;
        }
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Wraps a plain <see cref="IDialogStepBuilder"/> (name only, no <c>Build</c>) so it
    /// can participate in the registry. Template pipeline recognises this wrapper and skips
    /// dialog emission for it; DLG001 validation passes because the name is present.
    /// </summary>
    private sealed class NameOnlyMsiDialogStepBuilder(string name) : IMsiDialogStepBuilder
    {
        public string Name => name;

        public MsiDialogModel Build(DialogBuildContext context)
            => throw new InvalidOperationException(
                $"Dialog step '{name}' was registered as a name-only IDialogStepBuilder and cannot produce an MsiDialogModel. " +
                $"Implement IMsiDialogStepBuilder to supply dialog layout.");
    }
}
