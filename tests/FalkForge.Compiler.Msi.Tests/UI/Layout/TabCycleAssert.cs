using System;
using System.Collections.Generic;
using System.Linq;
using FalkForge.Compiler.Msi.UI;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout;

/// <summary>
/// Test-only helper asserting that a dialog's controls form exactly one closed Control_Next tab
/// cycle over its focusable subset — the invariant the Windows Installer Control table docs
/// require ("The links between the controls must form a closed cycle") and that no ICE checks,
/// because this repo authors no <c>_Validation</c> table (ICE03 only fires against one).
/// </summary>
internal static class TabCycleAssert
{
    // Duplicated ON PURPOSE from FalkForge.Compiler.Msi.UI.DialogTabCycle's exclusion set, rather
    // than referencing it: a test that imports the PRODUCTION exclusion list cannot catch a WRONG
    // exclusion list (e.g. if DialogTabCycle accidentally excluded PushButton, importing its own
    // list would make this helper agree with the bug instead of catching it). Mirrors WiX's
    // Compiler_UI.cs ParseControlElement notTabbable set (Billboard/ListView have no
    // MsiControlType member in this repo, so they are omitted here).
    private static readonly HashSet<string> NonFocusableTypeNames = new(StringComparer.Ordinal)
    {
        "Text", "Line", "Bitmap", "Icon", "ProgressBar", "GroupBox", "VolumeCostList",
    };

    /// <summary>Asserts the invariant against a composed <see cref="MsiDialogModel"/>.</summary>
    public static void AssertSingleClosedCycle(MsiDialogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var controls = model.Controls
            .Select(c => (Name: c.Name, TypeName: c.Type.ToString(), Next: c.NextControl))
            .ToList();

        AssertSingleClosedCycle(controls);
    }

    /// <summary>Asserts the invariant against row-shaped data (e.g. emitted Control table rows).</summary>
    public static void AssertSingleClosedCycle(IReadOnlyList<(string Name, string TypeName, string? Next)> controls)
    {
        ArgumentNullException.ThrowIfNull(controls);

        // 1. Every NON-focusable control has Control_Next == null — the docs' "static text
        // controls... may be left out of the cycle" allowance.
        foreach (var c in controls)
        {
            if (!IsFocusable(c.TypeName))
            {
                Assert.True(c.Next is null, $"Non-focusable control '{c.Name}' ({c.TypeName}) must not have a Control_Next.");
            }
        }

        var focusable = controls.Where(c => IsFocusable(c.TypeName)).ToList();

        // 2. Zero or one focusable control: nothing to link — every focusable control (there is at
        // most one) must also be null, and there is no cycle to walk.
        if (focusable.Count <= 1)
        {
            foreach (var c in focusable)
            {
                Assert.True(c.Next is null, $"Single focusable control '{c.Name}' must not self-loop.");
            }

            return;
        }

        var byName = focusable.ToDictionary(c => c.Name, c => c, StringComparer.Ordinal);

        // 3. OUT-DEGREE 1: every focusable control names a focusable control in the SAME dialog.
        // Catches dead ends (null Control_Next) and dangling references (points outside the
        // dialog or at a non-focusable control) — neither of which ICE can catch here.
        foreach (var c in focusable)
        {
            Assert.True(c.Next is not null, $"Focusable control '{c.Name}' has a null Control_Next.");
            Assert.True(byName.ContainsKey(c.Next),
                $"Control '{c.Name}' -> Control_Next '{c.Next}' is not a focusable control in this dialog.");
        }

        // 4. IN-DEGREE EXACTLY 1: each focusable control is named as a Control_Next target by
        // exactly one control. Catches two controls sharing a successor.
        var inDegree = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var c in focusable)
        {
            inDegree.TryGetValue(c.Next!, out int n);
            inDegree[c.Next!] = n + 1;
        }

        foreach (var c in focusable)
        {
            inDegree.TryGetValue(c.Name, out int n);
            Assert.True(n == 1, $"Control '{c.Name}' has in-degree {n}, expected exactly 1.");
        }

        // 5. SINGLE ORBIT. Steps 3+4 alone only prove a disjoint union of cycles — e.g. A->B->A
        // plus C->D->C both independently satisfy out-degree-1 and in-degree-1, yet that is TWO
        // cycles, not one closed cycle over the whole focusable set. Walking exactly
        // focusable.Count hops from focusable[0] and requiring the visited set to equal the full
        // focusable name set is what proves there is exactly ONE orbit.
        var visited = new HashSet<string>(StringComparer.Ordinal);
        string current = focusable[0].Name;
        for (int i = 0; i < focusable.Count; i++)
        {
            Assert.True(visited.Add(current), $"Control '{current}' was visited twice while walking the cycle — not a single orbit.");
            current = byName[current].Next!;
        }

        Assert.Equal(focusable[0].Name, current);
        Assert.Equal(focusable.Count, visited.Count);
        Assert.Equal(new HashSet<string>(byName.Keys, StringComparer.Ordinal), visited);
    }

    private static bool IsFocusable(string typeName) => !NonFocusableTypeNames.Contains(typeName);
}
