using System;
using System.Buffers;
using System.Runtime.InteropServices;

namespace FalkForge.Compiler.Msi.UI;

/// <summary>
/// Assigns <see cref="MsiControlModel.NextControl"/> so every composed dialog authors a single
/// closed tab cycle over its focusable controls.
/// </summary>
/// <remarks>
/// <para>
/// Per the Windows Installer Control table docs: "If the focus in the dialog box is on the
/// control in the Control column, hitting the tab key moves the focus to the control listed in
/// the Control_Next column... The links between the controls must form a closed cycle. Some
/// controls, such as static text controls, can be left out of the cycle. In this case, this field
/// may be left blank." Before this type existed, <see cref="Layout.DialogComposer"/> never set
/// <c>Control_Next</c> at all, so every stock dialog shipped an all-NULL chain — not "no cycle
/// needed", but a keyboard-navigation dead end for any control that is not the seeded default.
/// </para>
/// <para>
/// Focusable/non-focusable matches WiX's documented compiler behaviour, which marks Billboard,
/// Bitmap, GroupBox, Icon, Line, ProgressBar, Text, and VolumeCostList as not tabbable. This repo's
/// <see cref="MsiControlType"/> has no Billboard/ListView member, so the excluded set here is
/// exactly Text, Line, Bitmap, Icon, ProgressBar, GroupBox, VolumeCostList — see
/// <see cref="IsFocusable"/>. WiX also exposes an author-facing <c>TabSkip="yes"</c> escape hatch;
/// the Windows Installer Control Attributes table has no such bit, so there is deliberately no
/// equivalent knob here — the type-based exclusion is the only lever, and a dialog that wants a
/// control skipped (e.g. MsiRMFilesInUse's process <c>List</c>) keeps it in the cycle on purpose
/// rather than opting out.
/// </para>
/// <para>
/// Cycle order is GEOMETRIC — Y ascending, then X ascending, then declaration index as a final
/// tiebreak — rather than declaration order. <see cref="Layout.RightPackedRegionLayout"/> packs
/// the FIRST-declared control of a region against the region's RIGHT edge, and every three-button
/// wizard footer declares its buttons <c>Cancel, Next, Back</c> in that order, so a
/// declaration-order cycle would tab Cancel -> Next -> Back — backwards against the on-screen
/// Back/Next/Cancel row. The (Y, X, index) key is a total order over the control list (no two
/// controls share a key unless they share an index, which cannot happen), so the sort needs no
/// stability guarantee.
/// </para>
/// <para>
/// No ICE validates any of this: ICE03 would check <c>Control_Next</c> as a foreign key, but only
/// via the <c>_Validation</c> table, and this repo authors no <c>_Validation</c> table. The tests
/// that assert a single closed orbit are therefore the only gate against a dangling reference or a
/// broken half-cycle.
/// </para>
/// </remarks>
internal static class DialogTabCycle
{
    /// <summary>
    /// Links <see cref="MsiControlModel.NextControl"/> across every focusable control in
    /// <paramref name="model"/> into one closed ring, in top-to-bottom, left-to-right order.
    /// </summary>
    /// <remarks>
    /// All-or-nothing: if ANY control on the dialog already carries a non-null
    /// <see cref="MsiControlModel.NextControl"/> (an author-supplied chain — e.g. a
    /// <c>CustomDialogModel</c> control with <c>NextControl</c> set), this method returns
    /// immediately and leaves the entire dialog untouched. Completing a partial chain
    /// automatically is exactly how a broken half-cycle gets manufactured, so any authored link
    /// opts the whole dialog out of automatic assignment.
    /// </remarks>
    public static void Assign(MsiDialogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var controls = model.Controls;
        int count = controls.Count;

        for (int i = 0; i < count; i++)
        {
            // Empty/whitespace does not count as an authored chain — it carries no author intent
            // and (via DialogSetProducer.Rows.cs's StringOrNull) would otherwise ship a
            // Control_Next cell pointing at a control literally named "". Treating it as absent
            // lets auto-wiring proceed and overwrite the blank placeholder with a real link.
            if (!string.IsNullOrWhiteSpace(controls[i].NextControl))
            {
                return;
            }
        }

        if (count == 0)
        {
            return;
        }

        // Worst case every control is focusable, so the entry buffer is sized to the full control
        // count. Stock dialogs top out around a dozen controls; ArrayPool covers any larger
        // author-defined custom dialog without an unbounded stackalloc.
        const int StackThreshold = 32;

        // Declared-and-initialized in one statement (the idiomatic safe pattern): a stackalloc
        // assigned to a variable declared earlier, even unconditionally, trips CS8353 because its
        // safe-to-escape scope no longer matches the variable's declaring scope.
        Span<FocusEntry> stackBuffer = stackalloc FocusEntry[StackThreshold];
        FocusEntry[]? rented = count > StackThreshold ? ArrayPool<FocusEntry>.Shared.Rent(count) : null;
        Span<FocusEntry> entryBuffer = rented is not null ? rented.AsSpan(0, count) : stackBuffer[..count];

        try
        {
            int focusableCount = 0;
            for (int i = 0; i < count; i++)
            {
                if (IsFocusable(controls[i].Type))
                {
                    entryBuffer[focusableCount++] = new FocusEntry(controls[i].Y, controls[i].X, i);
                }
            }

            if (focusableCount <= 1)
            {
                // Zero or one focusable control: nothing to link, or a self-loop that WiX itself
                // guards against (firstControl != lastTabSymbol.Control) — leave Control_Next null.
                return;
            }

            Span<FocusEntry> focusable = entryBuffer[..focusableCount];

            // (Y, X, OriginalIndex) is pre-packed into the span itself so the comparison below
            // needs no closure over `controls` — a zero-capture static lambda, unlike a capturing
            // one, allocates no display class or delegate per dialog per build (Gate 6; matches
            // the static-lambda Sort pattern in PackageCodeDerivation.cs and the sequence table
            // producers).
            focusable.Sort(static (a, b) =>
            {
                int byY = a.Y.CompareTo(b.Y);
                if (byY != 0)
                {
                    return byY;
                }

                int byX = a.X.CompareTo(b.X);
                if (byX != 0)
                {
                    return byX;
                }

                return a.OriginalIndex.CompareTo(b.OriginalIndex);
            });

            for (int i = 0; i < focusableCount; i++)
            {
                int currentIndex = focusable[i].OriginalIndex;
                int nextIndex = focusable[(i + 1) % focusableCount].OriginalIndex;
                controls[currentIndex].NextControl = controls[nextIndex].Name;
            }
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<FocusEntry>.Shared.Return(rented);
            }
        }
    }

    // Packs (Y, X, OriginalIndex) for the geometric sort so the Comparison<T> passed to
    // Span<T>.Sort needs no closure over the control list — see Assign's remarks.
    [StructLayout(LayoutKind.Auto)]
    private readonly struct FocusEntry(int y, int x, int originalIndex)
    {
        public int Y { get; } = y;
        public int X { get; } = x;
        public int OriginalIndex { get; } = originalIndex;
    }

    // Mirrors WiX Compiler_UI.cs's notTabbable set (Billboard/ListView have no MsiControlType
    // member in this repo, so they are omitted here). Everything else is focusable.
    private static bool IsFocusable(MsiControlType type) => type switch
    {
        MsiControlType.Text => false,
        MsiControlType.Line => false,
        MsiControlType.Bitmap => false,
        MsiControlType.Icon => false,
        MsiControlType.ProgressBar => false,
        MsiControlType.GroupBox => false,
        MsiControlType.VolumeCostList => false,
        _ => true,
    };
}
