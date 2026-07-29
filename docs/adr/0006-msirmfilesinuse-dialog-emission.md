# 6. Authoring the MsiRMFilesInUse dialog for Restart Manager

- Status: Accepted
- Date: 2026-07-29
- Deciders: Peter Falkesand

## Context

`PackageBuilder.EnableRestartManagerSupport()` sets `PackageModel.EnableRestartManager`, which
`PropertyTableProducer` uses to author `MSIRMSHUTDOWN=0` in the `Property` table
(`src/FalkForge.Compiler.Msi/Recipe/Producers/PropertyTableProducer.cs:56-63`). There is no separate
built-in "`MsiRMFilesInUse` action" — per Microsoft's own
[`MsiRMFilesInUse` dialog documentation](https://learn.microsoft.com/windows/win32/msi/msirmfilesinuse-dialog),
"this dialog box will be created as required by the `InstallValidate` action." At full UI,
`InstallValidate` queries Restart Manager for files in use and, when it finds any, creates the
`MsiRMFilesInUse` dialog to ask the user whether Restart Manager should close and restart the
owning applications — but only if that dialog is actually authored in the package. `MSIRMSHUTDOWN`
is a separate, orthogonal control: it governs *how* Restart Manager shuts processes down once the
user has agreed (or, at silent/basic UI where there is no dialog to ask, unconditionally) — 0 shuts
down every affected process/service, 1 additionally forces unresponsive ones, 2 shuts down only if
every one of them registered for restart. Without an authored `MsiRMFilesInUse` dialog,
`InstallValidate` has nothing to create at full UI, so the install silently falls back to the
legacy `FilesInUse`/reboot-required path — meaning `EnableRestartManagerSupport()` had no observable
effect on the default double-click (full UI) install path before this change.

Separately, `CustomControlType.RadioButtonGroup`
(`src/FalkForge.Core/Models/CustomControlType.cs:49`) has been an authorable control type since it
was added, and `CustomDialogRules.cs:31` accepts it in author-supplied custom dialogs, but nothing
in the recipe pipeline ever emitted an MSI `RadioButton` table — the table ICE34 requires to exist
with one row per `RadioButtonGroup` value. A prior commit on this branch
(`fcca35b4`, "emit the RadioButton table for RadioButtonGroup controls") added the table's schema,
CREATE TABLE SQL, and the `DialogContent.RadioButtons` → `MsiDialogModel` → `RadioButton` table
plumbing, but left every emitted table empty — no dialog populated it yet. This ADR's dialog is the
first (and, as of this change, only) consumer of that plumbing: `MsiRMFilesInUseDlgBuilder`'s
`ShutdownOption` control is a `RadioButtonGroup` bound to `FalkForgeRMOption`, with two
`RadioButton` rows (`UseRM` order 1, `DontUseRM` order 2).

**This does not fully close the `RadioButtonGroup`/ICE34 gap.** `CustomDialogTranslator.Translate`
(`src/FalkForge.Compiler.Msi/UI/CustomDialogTranslator.cs`), the translator for the public
`AddCustomDialog`/`CustomDialogBuilder` authoring surface, still does not populate `RadioButton`
rows from a `CustomControlType.RadioButtonGroup` control — `CustomDialogBuilder.Control`'s XML doc
comment (`src/FalkForge.Core/Builders/CustomDialogBuilder.cs:167`) still warns "remember such
controls may need a companion table for their options." A package author who reaches for
`RadioButtonGroup` via `AddCustomDialog` today still ships an MSI that fails ICE34; only the
internal, declarative `DialogContent`-based dialogs (the five stock templates and now
`MsiRMFilesInUse`) can populate the table. Wiring the public authoring surface is out of scope for
this change and remains open.

## Decision

We made three decisions in authoring this dialog.

### 1. Gate emission on `EnableRestartManagerSupport()`, not always-on

The dialog is appended only when `package.EnableRestartManager` is `true`
(`DialogSetProducer.cs:175`), for the same reasons `MSIRMSHUTDOWN` is gated on that flag:

- It is the same predicate `PropertyTableProducer` already uses for `MSIRMSHUTDOWN`, so the two can
  never disagree — a package that sets one gets the other, by construction, not by two independent
  authors remembering to keep them in sync.
- It makes the existing flag mean something end-to-end for the first time: before this change,
  setting `MSIRMSHUTDOWN=0` with no dialog authored was a no-op at full UI.
- Every package that does *not* opt in keeps byte-identical MSI output — no new dialog, no new
  `Control`/`ControlEvent` rows, and no `RadioButton` table at all: that table is emitted only when
  at least one dialog actually populates it (see the RadioButton-table fix accompanying this ADR),
  so a package that never enables Restart Manager carries none of this feature's tables or rows —
  no reproducibility or parity-baseline churn for packages that never asked for it.
- It avoids handing every package an unconditional ICE34 authoring burden (a `RadioButtonGroup`
  needing a matching `Property` default) for a feature most packages will never use.
- It leaves bundle authors a suppression lever: a child MSI that should not offer the Restart
  Manager prompt (for example a silent-only component) simply does not call
  `EnableRestartManagerSupport()`.

### 2. Append the dialog at producer level, not declare it in each stock template

`DialogSetProducer.Produce` appends `MsiRMFilesInUseDlgBuilder.Build()` once, after composing
whichever stock template is active, rather than adding it to each of the five
`IDialogTemplate` implementations individually
(`MinimalDialogTemplate`, `InstallDirDialogTemplate`, `FeatureTreeDialogTemplate`,
`MondoDialogTemplate`, `AdvancedDialogTemplate`).

This deliberately breaks the precedent set when `CancelDlg` and `BrowseDlg` were added to all five
templates: those are flow dialogs, reachable by `SpawnDialog` from a sibling dialog within the
template's own wizard chain, so they belong to each template's dialog set by definition.
`MsiRMFilesInUse` is reachable from nothing in the authored dialog set — the Windows Installer
engine creates it directly from `InstallValidate`, matching `CancelDlgBuilder`'s self-contained
modal pattern rather than a wizard step. Its presence or absence has nothing to do with which
template is active and everything to do with whether Restart Manager support is enabled, so one
insertion point at the producer covers all five stock sets, keeps the five template files and their
existing tests untouched, and avoids five copies of the same conditional.

### 3. Order `ControlEvent` rows RM-then-end, not WiX's end-then-RM

WiX's own `MsiRMFilesInUse.wxs` publishes `EndDialog` on OK before `RMShutdownAndRestart`. This
builder orders them the other way: `RMShutdownAndRestart` (argument `0`, condition
`FalkForgeRMOption~="UseRM"`) fires at Ordering 1, `EndDialog` (argument `Return`) fires at
Ordering 2 (`MsiRMFilesInUseDlgBuilder.cs:60-82`).

This is a readability choice, not a correctness fix, and there is no established functional
difference between the two orders. The
[`ControlEvent` table documentation](https://learn.microsoft.com/en-us/windows/win32/msi/controlevent-table)
states only that "the installer starts each event in the order specified in the Ordering column" —
that sentence is order-neutral: it says events fire in `Ordering` sequence, but it does not say
which of the two orders is required, and both an RM-then-end and an end-then-RM ordering satisfy it
equally. WiX has shipped its end-then-RM order for years with no evidence it fails to invoke
Restart Manager, so there is nothing to suggest the two orders behave differently at runtime. We
order RM-then-end here purely because it reads in the causal order the two events actually
happen — Restart Manager acts, then the dialog ends — not because the other order is wrong.

**A future reader who checks the reference implementation may be tempted to "align" this back to
WiX's order. Either order works; this note exists only so that reader isn't left guessing why our
copy differs, not to forbid the change.**

## Consequences

- `EnableRestartManagerSupport()` now has an observable effect on the default (full UI,
  double-click) install path, not only on silent/basic-UI installs where `MSIRMSHUTDOWN` alone was
  already sufficient.
- Five new localization keys (`Dialog.RestartManager.Title`, `.Description`, `.Text`, `.CloseApps`,
  `.DontCloseApps`) exist in `en-US.json`, `sv-SE.json`, and as internal defaults
  (`DialogSetProducer.Localization.cs`), so a package supplying its own `LocalizationData` without
  these keys still builds instead of failing on an unresolved `!(loc.X)` reference.
- **ICE34 compliance for this dialog is not verified on this development machine.** No `darice.cub`
  is installed locally; the lenient `IceValidator` overload returns success when the cub file is
  missing rather than failing the build, so a local `dotnet test`/`forge verify` run cannot prove
  ICE34 cleanliness. CI provisions WiX (and therefore `darice.cub`) and is the actual point of
  verification for this change — a green CI run, not a green local run, is the evidence that matters
  here.
- The `RadioButtonGroup`/ICE34 gap for the public `AddCustomDialog` authoring surface remains open:
  `CustomDialogTranslator` still does not populate `RadioButton` rows for an author's own
  `RadioButtonGroup` control. A future change would need to either extend `CustomDialogBuilder` with
  a `RadioButton`-row adder or extend the translator to source rows from a new field on
  `CustomDialogControlModel`.
- Decision 3's RM-then-end ordering is a readability choice with no established functional
  difference from WiX's end-then-RM order — switching to WiX's order is a valid stylistic change,
  not a regression, though it should be done deliberately (re-reading this ADR first) rather than
  by silently copying WiX's structure without noticing the deviation.
- Whether the dialog actually functions at runtime (appears, closes the locked app, exits 0 instead
  of 3010) cannot be proven by any unit test — it needs one manual run on a machine the author
  controls: hold a file open, install at full UI, and observe the dialog, the app closing, and the
  exit code.
