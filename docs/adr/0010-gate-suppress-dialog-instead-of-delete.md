# 10. Gate the inert SuppressDialog API instead of deleting or implementing it

- Status: Accepted
- Date: 2026-08-01
- Deciders: Peter Falkesand

## Context

`DialogCustomization.SuppressDialog(StockDialog)` (`src/FalkForge.Core/Models/DialogCustomization.cs`)
and the model field it feeds, `DialogCustomizationModel.SuppressedDialogs`
(`src/FalkForge.Core/Models/DialogCustomizationModel.cs`), have existed since the dialog
customization RFC. Nothing downstream ever consumed the set:

- `DialogComposer.Compose` (`src/FalkForge.Compiler.Msi/UI/Layout/DialogComposer.cs`) says so in
  its own remarks — suppression is "not applied here — that is a dialog-set-level concern handled
  by the emitter that decides which dialogs to compose at all."
- No such emitter exists. Every `IDialogTemplate` (`InstallDirDialogTemplate`,
  `MinimalDialogTemplate`, `FeatureTreeDialogTemplate`, `MondoDialogTemplate`,
  `AdvancedDialogTemplate`) unconditionally composes and returns all of its dialogs; none of them
  read `PackageModel.DialogCustomization.SuppressedDialogs` at all.

So `SuppressDialog(StockDialog.License)` compiled, ran, and populated the model field, and the
compiled MSI still shipped the License dialog unchanged — a public API that silently does nothing,
the same defect shape as several prior findings in this codebase (`SetProperty`,
`MsiCompiler(IFileSystem)`, reserved ports — see the 2026-07-27 top-5 remediation).

The only code that read `SuppressedDialogs` at all was the DLG002 validation rule
(`DialogCustomizationValidator.Validate`), which rejected a suppression if the target dialog was a
navigation target of another dialog in the same template, per a hand-built
`ProtectedDialogs`/`BuildProtectedDialogs` lookup table. That table was itself wrong: its
`MsiDialogSet.InstallDir` entry was `{Welcome, InstallDir, Progress, Exit}`, omitting `License`,
even though `InstallDirDialogTemplate.GetDialogs` (line 28) wires `Welcome`'s `NextDialog` to
`DialogNames.LicenseAgreement` — `License` unambiguously is a navigation target in that template. A
package author suppressing `License` under `InstallDir` would have received **no DLG002 error** for
a suppression that (had suppression actually been implemented) would have broken the wizard's
navigation chain. The rule that was supposed to be the safety net for this feature had a
demonstrable gap in the one template most likely to use it.

Two closable surfaces exist for "stop authors from using this":

1. The fluent builder method, `DialogCustomization.SuppressDialog`.
2. `DialogCustomizationModel.SuppressedDialogs` itself — a public record with `{ get; init; }`, so
   an author can populate it with an object initializer or a `with` expression and never call the
   builder method at all. Gating only the method leaves this door open.

## Decision

Peter's decision, verbatim: *"Remove the possibility to use the api, but don't delete it if any of
the code is usable. Add to the plan that it should be fixed later so that we don't miss it."*
That is: make the API unusable now, keep whatever plumbing is reusable, and leave an explicit
pointer to the real implementation (tracked as task #44) so this is not silently forgotten.

We close both surfaces:

1. **`DialogCustomization.SuppressDialog` is marked `[Obsolete(..., error: true)]`.** This is a
   genuine compiler error (CS0619) for any caller, not a warning that could be missed or suppressed
   — verified with an in-memory Roslyn compile of a snippet that calls it
   (`DialogCustomizationSuppressDialogGateTests`), the same technique
   `EmittedSourceCompilesTests` already uses elsewhere in this repo to prove generated code
   compiles. Marking `[Obsolete]` on any member trips Sonar's S1133 ("remove this deprecated code
   someday") under this repo's `TreatWarningsAsErrors` build; that is suppressed with a
   `[SuppressMessage("Sonar", "S1133", Justification = ...)]` citing this ADR and task #44, because
   S1133 is a reminder rule that fires on *any* intentional `Obsolete`, not a defect detector — the
   suppression records the decision rather than hiding a problem.

2. **DLG002 is replaced.** It no longer consults a protected-dialog table; it now fails the build
   whenever `DialogCustomizationModel.SuppressedDialogs` is non-empty, unconditionally, regardless
   of how the set was populated. This is the gate that actually holds against the object-initializer
   path, since `SuppressedDialogs` cannot itself be marked `Obsolete` without also breaking
   `DialogCustomization.ToModel()`'s own (legitimate, in-assembly) write to it.

The old `ProtectedDialogs`/`BuildProtectedDialogs` table was deleted rather than kept dead
(`CA1823`/`IDE0052` fail an unread private field under this build's analyzer set, and this
codebase's convention is to fix or delete rather than add a suppression pragma for a
gate-defeating warning). Its content — five `MsiDialogSet` → navigation-target-`StockDialog`-set
entries, including the now-confirmed-wrong `InstallDir` entry — is preserved verbatim below, in
this ADR's Consequences section, with an explicit note that it should be re-derived from the
templates rather than pasted back as-is, since it was wrong. It is also recorded in task #44's
notes as a secondary copy.

We did not implement real suppression. Composing a subset of dialogs while keeping the wizard's
`NewDialog`/`SpawnDialog` chain internally consistent (the exact defect DLG002 was trying, and
failing, to catch) is a nontrivial per-template change that task #44 owns.

## Consequences

- `DialogCustomization.SuppressDialog(...)` no longer compiles anywhere in this repo or in any
  downstream project — verified by CS0619 in a real Roslyn compilation, not just by reading the
  attribute.
- `DialogCustomizationModel.SuppressedDialogs` remains a normal public `init` property (needed
  intact for #44), but any package that populates it — through the builder or directly — now fails
  the build at DLG002, with a message naming the suppressed dialog, the template, and task #44.
- Existing tests that called `SuppressDialog(...)` fluently (`DialogCustomizationTests`,
  `PackageBuilderDialogCustomizationTests`) were rewritten to stop calling it; the model-level
  `SuppressedDialogs` coverage (`DialogCustomizationModelTests`, object-initializer/`with`-based)
  is untouched, since that path deliberately stays open at the model layer and is rejected one
  layer up, at the validator.
- `DialogCustomizationValidatorTests`'s DLG002 section was rewritten for the new unconditional
  semantics, including a regression test (`DLG002_any_suppressed_dialog_returns_error`, suppressing
  `Maintenance`, which the old table treated as "safe") and a test naming the object-initializer
  path explicitly, so a reader reverting to the old table-based check sees these go red.
- `documentation.html`, `docs/dialog-template-architecture.md`, and
  `docs/validation-error-codes.md` all previously described DLG002 or `SuppressDialog` in ways
  that did not match the shipped behavior even before this change (the `documentation.html` row
  described an entirely different rule — duplicate `InsertedDialogStep` anchors — that no code in
  this file has ever implemented). All three now state plainly that `SuppressDialog` does not work
  yet and point at task #44.
- Task #44 inherits: the real suppression implementation, plus the corrected (or re-derived)
  per-template navigation-target data DLG002 used to rely on, plus deciding whether the eventual
  real DLG002 should resemble the deleted navigation-aware check or take a different shape
  entirely once dialogs can actually be omitted from composition.

### Deleted `ProtectedDialogs` table (recovered verbatim from git history)

The table below is the exact content of `BuildProtectedDialogs()` as it existed in
`src/FalkForge.Compiler.Msi/UI/DialogCustomizationValidator.cs` immediately before this change
deleted it. It is reproduced here — not merely referenced — because a document that only points
at another document as the source of truth, when that other document does not actually contain
the data, is the same defect this ADR exists to gate against. The `InstallDir` entry's error is
annotated in place; do not re-copy it as-is into any future implementation — re-derive per-template
navigation targets from the templates themselves (`InstallDirDialogTemplate`,
`FeatureTreeDialogTemplate`, `MondoDialogTemplate`, `AdvancedDialogTemplate`,
`MinimalDialogTemplate`).

```csharp
private static FrozenDictionary<MsiDialogSet, FrozenSet<StockDialog>> BuildProtectedDialogs()
{
    // Each template's protected set is the union of all dialogs that appear as
    // NewDialog targets in that template's event wiring. These were extracted from
    // the builder DialogFlowContext chains in FeatureTreeDialogTemplate,
    // InstallDirDialogTemplate, MondoDialogTemplate, AdvancedDialogTemplate, and
    // MinimalDialogTemplate.
    //
    // Entry-point dialogs (Welcome) are also protected because they are referenced
    // from the install sequence Execute action to start the UI sequence.
    return new Dictionary<MsiDialogSet, FrozenSet<StockDialog>>
    {
        [MsiDialogSet.Minimal] = FrozenSet.Create(
            StockDialog.Welcome,    // UI sequence entry point
            StockDialog.Progress,   // target of Welcome->Next
            StockDialog.Exit),      // target of Progress completion

        [MsiDialogSet.InstallDir] = FrozenSet.Create(
            StockDialog.Welcome,    // UI sequence entry point
            StockDialog.InstallDir, // target of Welcome->Next   <-- WRONG: the real target is License
            StockDialog.Progress,   // target of InstallDir->Next (Install)
            StockDialog.Exit),      // target of Progress completion

        [MsiDialogSet.FeatureTree] = FrozenSet.Create(
            StockDialog.Welcome,    // UI sequence entry point
            StockDialog.License,    // target of Welcome->Next
            StockDialog.Features,   // target of License->Next
            StockDialog.Progress,   // target of Customize->Next (Install)
            StockDialog.Exit),      // target of Progress completion

        [MsiDialogSet.Mondo] = FrozenSet.Create(
            StockDialog.Welcome,    // UI sequence entry point
            StockDialog.License,    // target of Welcome->Next
            StockDialog.InstallDir, // target of SetupType->Next (one branch)
            StockDialog.Features,   // target of SetupType->Next (another branch)
            StockDialog.Progress,   // target of InstallDir/Features->Next (Install)
            StockDialog.Exit),      // target of Progress completion

        [MsiDialogSet.Advanced] = FrozenSet.Create(
            StockDialog.Welcome,    // UI sequence entry point
            StockDialog.License,    // target of Welcome->Next
            StockDialog.InstallDir, // navigation target
            StockDialog.Features,   // navigation target
            StockDialog.Progress,   // target of install branch
            StockDialog.Exit),      // target of Progress completion
    }.ToFrozenDictionary();
}
```
