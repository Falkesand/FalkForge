# 7. Automatic Control_Next tab-cycle authoring

- Status: Accepted
- Date: 2026-07-31
- Deciders: Peter Falkesand

## Context

`DialogComposer` built every stock and custom dialog's `Control` rows but never set
`Control_Next`, so every composed dialog shipped with an all-NULL tab chain. Per the
[Control table documentation](https://learn.microsoft.com/en-us/windows/win32/msi/control-table):
"If the focus in the dialog box is on the control in the Control column, hitting the tab key moves
the focus to the control listed in the Control_Next column... The links between the controls must
form a closed cycle. Some controls, such as static text controls, can be left out of the cycle. In
this case, this field may be left blank." A `NULL` `Control_Next` therefore means "not in the
cycle" for a control that legitimately has no business receiving focus — it does not mean "cycle
ends here" for a control that does. Before this change every dialog had the latter reading by
default, for every control.

The concrete symptom: on `MsiRMFilesInUse` (see ADR 0006), the `ShutdownOption`
`RadioButtonGroup` — the control that lets a user decline having Restart Manager close their
applications — was unreachable by Tab from `Control_First`. A keyboard-only user was stuck on the
seeded `FalkForgeRMOption=UseRM` default with no way to reach the control that would let them
change it.

No ICE check catches this. ICE03 validates `Control_Next` as a foreign key, but only by reading
the package's own `_Validation` table; this repo does not author a `_Validation` table (a MSI SDK
resource, not build output), so ICE03 never runs against this column here. The graph-invariant
tests added alongside this ADR (`TabCycleAssert`, `DialogTabCycleTests`, and the row-level theories
in `DialogSetProducerTests`) are the only thing verifying the cycle is closed.

## Decision

`DialogTabCycle.Assign(MsiDialogModel model)`
(`src/FalkForge.Compiler.Msi/UI/DialogTabCycle.cs`) links every focusable control in a composed
dialog into one closed ring, in top-to-bottom/left-to-right order. It is called once, from
`DialogSetProducer.Produce`, over the fully-assembled dialog list — after stock templates, the
Restart Manager `MsiRMFilesInUse` dialog, author-defined custom dialogs, and extension-contributed
steps have all been appended, and before the tables are built. That single call site is the only
point where every dialog source converges, so no future dialog builder can forget to wire
`Control_Next`.

### 1. Geometric order, not declaration order

The cycle links controls sorted by `(Y ascending, X ascending, declaration index)`, not by the
order they were declared in the `DialogContent`. This is a hard requirement, not a style choice:
`RightPackedRegionLayout` (used by every stock `ButtonRow`) packs the **first-declared** control of
a region against the region's right edge and lays each subsequent control to its left. Every stock
footer declares its buttons `DialogFooter.CancelButton(), NextButton(), BackButton()` — so a
declaration-order tab cycle would tab `Cancel -> Next -> Back`, which reads backwards against the
on-screen `Back, Next, Cancel` row. Geometric order tabs `Back -> Next -> Cancel`, matching what
the user sees. The `(Y, X, index)` key is a total order over the control list (no two controls
share a key unless they share an index, which cannot happen), so the sort needs no stability
guarantee and uses an explicit `Comparison<int>` rather than relying on it.

### 2. `Control_First` stays exactly as authored

`DialogComposer`'s `FindFallbackFirstControl` and `CustomDialogTranslator.ResolveFirstControl`
already resolve `Control_First` independently of the tab cycle, and all 11 stock dialogs name a
real, focusable control. `DialogTabCycle.Assign` does not touch `Control_First` and does not derive
it from the cycle the way WiX's own compiler does. Instead, `DialogSetProducerTests` adds an
invariant test (`Produce_Control_First_names_a_control_in_the_tab_cycle`) asserting every emitted
`Control_First` names one of that dialog's own `Control` rows, rather than pinning a specific value
per dialog.

### 3. `MsiRMFilesInUse.List` stays in the cycle, unlike WiX

WiX's own `MsiRMFilesInUse.wxs` marks the in-use process list `TabSkip="yes"`, removing it from the
tab order. This repo deliberately keeps `List` (a `ListBox`) focusable and in the cycle: the list of
processes about to be closed is exactly the information a user needs to decide whether to allow it,
so skipping past it by keyboard would defeat the purpose of showing it. There is also no
`TabSkip`-equivalent bit anywhere in this repo: the Windows Installer Control Attributes table has
no such flag — `TabSkip` is a WiX-compiler-only concept implemented by simply omitting a control
from the linked list, which our type-based focusable/non-focusable split
(`Text`, `Line`, `Bitmap`, `Icon`, `ProgressBar`, `GroupBox`, `VolumeCostList` excluded; everything
else focusable, mirroring WiX's own `Compiler_UI.cs` `notTabbable` set) already provides as the only
lever. `MsiRMFilesInUse`'s ring is `List -> ShutdownOption -> OK -> Cancel -> List`, with
`Control_First = OK`.

### 4. All-or-nothing per dialog

If any control on a dialog already has a non-null `Control_Next` when `Assign` runs — an
author-supplied chain via `CustomDialogBuilder`'s `Next(controlName)`, or a `CustomDialogModel`
built directly with `NextControl` set — the entire dialog is left untouched. Completing a *partial*
chain automatically is exactly how a broken half-cycle gets manufactured (the untouched controls
would silently point nowhere, or the auto-linked ones would collide with the authored one), so any
authored link opts the whole dialog out of automatic wiring. This is documented on the public
`Next(controlName)` API in `documentation.html` since it is an author-visible behavior change.

### 5. A dialog with at most one focusable control gets no self-loop

Mirrors WiX's own guard (`firstControl != lastTabSymbol.Control`): `ProgressDlg` (one focusable
`Cancel` button) and `ExitDlg` (one focusable `Finish` button) emit `Control_Next = NULL` both
before and after this change — proven by exact-row-level test coverage, and a useful signal that
the change is otherwise surgical.

## Consequences

- Every stock dialog and `MsiRMFilesInUse` now authors a real, closed tab cycle; the keyboard-
  consent defect on `MsiRMFilesInUse.ShutdownOption` is fixed.
- `MsiControlModel.NextControl` changed from `{ get; init; }` to `{ get; set; }` so
  `DialogTabCycle.Assign` can link controls in place after the full list is known, mirroring the
  existing settable `Text` property (rewritten in place by localization for the same reason).
- **ICE cannot verify this.** ICE03 only checks `Control_Next` via a `_Validation` table this repo
  does not author. The graph-invariant tests (out-degree exactly 1, in-degree exactly 1, and a
  single-orbit walk over the whole focusable set — out/in-degree alone only prove a disjoint union
  of cycles, not one cycle) are the only gate against a dangling reference or a broken half-cycle,
  for every stock dialog set and for `MsiRMFilesInUse`.
- **Known follow-up, not fixed here:** `DialogComposer.FindFallbackFirstControl` and
  `CustomDialogTranslator.ResolveFirstControl` can, in principle, name a non-focusable control as
  `Control_First` on a dialog that declares no explicit `FirstControl` (falling back to "the first
  `PushButton` in `ButtonRow`" or "the first authored control" respectively). No stock dialog
  exercises this — every stock template's fallback path lands on a `PushButton` — so it is left as a
  documented gap rather than fixed speculatively.
