using System;
using System.Collections;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace FalkForge.Compiler.Msi.UI.Layout;

/// <summary>
/// Mutable registry of <see cref="IDialogStepBuilder"/> instances. Builders are registered
/// by name during the compilation setup phase and then frozen before the template pipeline
/// begins. After freezing, the registry is read-only and safe to share across threads.
/// </summary>
/// <remarks>
/// RFC Cycle 6, step 16. DLG001 validation uses the frozen registry to check that every
/// step name referenced by <see cref="FalkForge.Models.DialogCustomizationModel.InsertedSteps"/>
/// has a matching registered builder.
/// </remarks>
internal sealed class DialogStepRegistry : IEnumerable<IDialogStepBuilder>
{
    // Use Dictionary during registration phase; frozen on Freeze().
    private readonly Dictionary<string, IDialogStepBuilder> _builders =
        new(StringComparer.Ordinal);

    private FrozenDictionary<string, IDialogStepBuilder>? _frozen;
    private bool _isFrozen;

    /// <summary>Number of registered step builders.</summary>
    public int Count => _isFrozen ? _frozen!.Count : _builders.Count;

    /// <summary>
    /// Registers a step builder. Throws if the name is already registered or if the
    /// registry has been frozen.
    /// </summary>
    public void Register(IDialogStepBuilder builder)
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
    public bool TryGet(string name, out IDialogStepBuilder? builder)
    {
        ArgumentNullException.ThrowIfNull(name);

        var lookup = _isFrozen
            ? (IDictionary<string, IDialogStepBuilder>)_frozen!
            : _builders;

        return lookup.TryGetValue(name, out builder);
    }

    /// <inheritdoc/>
    public IEnumerator<IDialogStepBuilder> GetEnumerator()
    {
        var source = _isFrozen
            ? (IEnumerable<KeyValuePair<string, IDialogStepBuilder>>)_frozen!
            : _builders;

        foreach (var pair in source)
        {
            yield return pair.Value;
        }
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
