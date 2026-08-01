# 9. Runtime dependency enforcement (corrected record): supersedes ADR 8

- Status: Accepted
- Date: 2026-08-01
- Deciders: Peter Falkesand
- Supersedes: ADR-0008

## Context

ADR 0008 recorded the original decision for runtime dependency enforcement (fail-closed
uninstall/install detection, fail-open write side, scope-mirrored write with union read, a
narrow elevated companion allowlist). While that ADR sat on this feature branch — never yet
merged, but already carrying `Status: Accepted` — a Merge Gate review and a follow-up round of
fixes found that two of its factual claims were wrong: (1) `DetectUnsatisfiedProviders` did not
actually fail closed on an unreadable registry the way the uninstall-side check did, and (2) the
write-side "failure is contained" claim was true for the install direction but false for the
uninstall direction, where a failed unregister can permanently strand a hard block on an
unrelated third product. Both corrections were made **in place** in ADR 0008 (an "Amended" note
plus edited Decision-section text), each time reasoning that a factual correction is different in
kind from reversing a decision.

ADR 0001 does not draw that distinction. It states the rule unconditionally: *"An ADR is
immutable once Accepted... Never edit a decision away — the history is the point,"* and prescribes
exactly one mechanism for any change: a new ADR that supersedes the old one, with the old ADR's
status flipped to `Superseded by ADR-NNNN`. It does not carve out an exception for factual
corrections, and it does not condition immutability on the ADR having been merged to `main` or
read by anyone outside the branch — the trigger is the `Status: Accepted` field itself, which
ADR 0008 has carried since its creation. Two in-place edits under a "this is just a correction"
rationale is exactly the kind of unbounded precedent ADR 0001 exists to prevent: the next
contributor has no principled way to tell "acceptable correction" from "decision quietly
reversed" without re-deriving the same judgment call each time.

This ADR is the mechanism ADR 0001 actually prescribes: instead of a third in-place edit to
ADR 0008 (for a fifth follow-up fix — a `BundleId` ownership check on unregister — landing in the
same development cycle), the corrected, complete decision is recorded here as a fresh Accepted
ADR, and ADR 0008 is marked Superseded rather than touched again. ADR 0008's text is left as it
stood (including its own two in-place amendments) as the historical record of what was believed
and decided at each point — this ADR does not retroactively rewrite that history, it supersedes
it going forward.

## Decision

We adopt the following as the current, correct, complete decision for runtime dependency
enforcement — identical in substance to ADR 0008's intent, with the errors ADR 0008 accumulated
corrected and its follow-on hardening folded in:

**Enforce immediately, with an explicit `--ignore-dependencies` escape hatch shipped in the same
change.** Silently ignoring a still-depended-on provider is the defect being fixed; a warn-only
rollout or other half-measure reproduces it.

**Both uninstall and install-time detection fail closed on an unreadable registry**, worded
distinctly from a genuinely-missing/unsatisfied provider:
- `DependencyDetector.DetectBlockingDependencies` (uninstall side) propagates a registry read
  error on either `HKLM` or `HKCU` as a `Result` failure, which `PlanStep` turns into a hard
  refusal (`ErrorKind.PlanningError`). An unknown state must never be silently treated as "no
  dependants."
- `DependencyDetector.DetectUnsatisfiedProviders` (install side) reads via
  `IRegistry.TryGetStringValue` and likewise propagates a read error on either root as a
  `Failure`, which `PlanStep` turns into a "Cannot verify dependency state safely" refusal —
  worded distinctly from "required dependency provider(s) not satisfied" so an operator is never
  told a provider is missing when the truth is that its state could not be determined.

**The write side (`ApplyStep` registering/unregistering after a successful apply) fails open, but
the severity is direction-dependent, not symmetric.** A persistence failure (registry access
denied, elevated round-trip failure, no elevation companion available) never turns an
already-successful install/uninstall into a reported failure, on either direction — but:
- On INSTALL, a write failure is genuinely contained (nothing else was relying on the
  registration yet) and is logged at `LogLevel.Warning`.
- On UNINSTALL, a write failure is NOT contained: this bundle's own consumer entry survives the
  failed unregister, potentially forever, which can permanently strand a hard block on an
  unrelated THIRD product's future uninstall of the shared provider it referenced. This direction
  is logged at `LogLevel.Error` and names the exact `HKLM`/`HKCU` registry path(s) an operator
  needs to clear by hand.

**Scope-mirrored write, union read.** A `PerUser` bundle writes its own provider/consumer
registrations directly to `HKCU` (no elevation). A `PerMachine` bundle writes to `HKLM` through
the elevated companion. Both the uninstall and install checks read both roots regardless of the
current bundle's own scope, since a per-user consumer of a per-machine provider (or the reverse)
is a real, supported shape.

**A separate elevated command (`DependencyRegistrationCommand`), not a `RegistryWriteCommand`
carve-out**, with its own allowlist scoped to exactly `SOFTWARE\Classes\Installer\Dependencies\`.

**Every provider/consumer key segment is validated by one shared guard, called by every writer.**
`DependencyRegistrationPaths.IsSafeKeySegment` — anchored with `\A`/`\z` (never `^`/`$`, which
matches before a trailing newline in .NET) — is enforced inside `DependencyRegistrar` itself
(the single writer used by both the unprivileged `PerUser` path in `ApplyStep` and the elevated
`HKLM` companion), not only by the elevated command's own pre-check. This closes a gap where the
`PerUser` path built `DependencyRegistrar` directly with unvalidated manifest-sourced keys.

**Unregister checks ownership before deleting.** `DependencyRegistrationCommand` reads back the
`BundleId` value `RegisterConsumer` stamps into each consumer subkey and refuses (all-or-nothing,
before any write in the batch) to unregister an entry stamped with a different bundle's id. A
missing `BundleId` (no prior owner recorded — e.g. a pre-existing MSI-authored entry) is not
treated as a mismatch. Without this, an elevated command run on behalf of one bundle could delete
another bundle's own consumer registration and then freely uninstall a shared provider out from
under it.

**Silent mode does not imply `--ignore-dependencies`.** They are orthogonal flags; silent/
unattended uninstall is exactly where an unexpected silent break of a dependent product would hurt
most.

## Consequences

- An uninstall that previously always succeeded (the check was never wired at all) can now be
  genuinely refused — a real behavior change for any existing `DependencyProvider`/
  `DependencyConsumer` bundle, documented in release notes as something to audit before upgrading.
- MSI-authored dependencies already exist on some machines from earlier releases; nothing added
  by this change retroactively cleans those up. A stale MSI-authored consumer entry can block a
  bundle uninstall exactly as a stale bundle-authored one can. `--ignore-dependencies` is the
  immediate escape hatch; nothing currently collects an orphaned provider row once its last
  consumer is gone (the `BundleId` value stamped by `DependencyRegistrar` exists specifically so a
  future orphan-collection pass has something to key off — that pass is not built yet).
- **A stale provider row can make a MISSING dependency read as satisfied.** A provider's own
  uninstall removes only its `DependencyConsumers` entries — its own provider row (the one
  `DetectUnsatisfiedProviders` reads `Version` from) is never removed, deliberately: the same
  provider key can legitimately be written by more than one product, so there is no way to tell
  "the last provider of this key just uninstalled" from "another one is still present" without a
  provider-side reference-counting mechanism that does not exist today. Unconditionally deleting
  the provider row on its own uninstall was considered and rejected — it would incorrectly
  un-satisfy the requirement for every OTHER product still legitimately providing the same key.
  The accepted trade-off: after the actual provider is gone, `DetectUnsatisfiedProviders` can read
  the leftover row and let a dependent install proceed against a component that is no longer
  present. `documentation.html` §9.3e documents both the stale-BLOCKER and stale-SATISFIED
  directions; "install refusal on missing dependency" is a weaker guarantee than release notes
  describing it in absolute terms have implied.
- **An elevated bundle can register a dependent under a provider key it does not own.**
  `DependencyRegistrationCommand`'s allowlist and traversal guard constrain WHERE in the registry
  a payload can write, not WHOSE provider key it claims to depend on. Once registered, that
  consumer entry legitimately (per this ADR's own reference-counting design) blocks a future
  uninstall of the other vendor's component until the entry is removed — an effective
  cross-vendor lockout. Contained by the fixed-prefix allowlist and by the same elevation consent
  any per-machine install already requires — not a privilege-escalation path, only a
  mis-scoped-trust one. `--ignore-dependencies` remains the operator's escape hatch.
- `DependencyRegistrationCommand` widens the elevated companion's SYSTEM-privileged attack
  surface by one command. Its allowlist is narrower than `RegistryWriteCommand`'s, and
  `ElevatedHostCommandSurfaceTests` pins the exact registered command set so a future addition is
  a reviewed, deliberate decision.
- Reversing the fail-closed/fail-open split, or the install/uninstall write-failure severity
  asymmetry, would need a new ADR superseding this one — this record is why the asymmetry is
  deliberate, not an oversight.
- Process consequence: ADR 0008 is retired to `Superseded by ADR-0009` rather than edited a third
  time. Future corrections to this decision follow the same path — a new superseding ADR — per
  ADR 0001, regardless of whether the correction is a reversal or a factual fix and regardless of
  whether the superseded ADR has reached `main` yet.
