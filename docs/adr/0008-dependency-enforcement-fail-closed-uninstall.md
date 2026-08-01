# 8. Runtime dependency enforcement: fail-closed uninstall, fail-open install-write, narrow companion allowlist

- Status: Superseded by ADR-0009
- Date: 2026-08-01
- Deciders: Peter Falkesand
- Amended: 2026-08-01 — see note at the end of the Decision section. `DetectUnsatisfiedProviders`
  (install-side detection) now fails closed on an unreadable registry too, via
  `IRegistry.TryGetStringValue`; the original text below asserting this "is not a concept that can occur"
  described a gap in the initial implementation, not a considered decision, and is corrected in place
  rather than superseded — the fail-closed-on-unknown-state principle this ADR records is unchanged, only
  completed on the side that was missing it.

## Context

`DependencyDetector.DetectBlockingDependencies` (uninstall must refuse when other installed packages
still depend on a shared component) and `DetectUnsatisfiedProviders` (install must refuse when a
required provider is missing) were fully implemented and unit-tested, but had zero callers anywhere in
`src/` — `BundleBuilder.DependencyProvider(...)`/`.DependencyConsumer(...)` recorded intent in the
manifest but nothing ever enforced it. The only existing writer of the
`SOFTWARE\Classes\Installer\Dependencies\` registry layout was `DependencyTableContributor`, an MSI
table contributor that emits Registry-table rows for Windows Installer's own writer to execute at MSI
install time — a compile-time mechanism, unrelated to the bundle-runtime engine. The bundle-runtime
write side (`InstallerManifest.DependencyConsumers` read by `ApplyStep`, or an `IRegistry` reachable
from `ApplyStep` at all) did not exist. Wiring only the read side — calling the detector from `PlanStep`
without ever writing a registration at `ApplyStep` — would make every check see an empty registry,
making every uninstall pass and every install's requirement look unsatisfied: the feature would *look*
fixed while remaining decoration.

Three design questions had to be settled together: (1) what happens to an uninstall/install when the
registry read genuinely cannot be trusted (permission denied, mid-write corruption); (2) whether a
write-side persistence failure should ever turn an otherwise-successful apply into a reported failure;
(3) how the elevated per-machine write reaches `HKLM` without reopening the COM/shell hijack surface
that `RegistryWriteCommand`'s existing allowlist deliberately closes by reserving `SOFTWARE\Classes\`.

## Decision

**We enforce immediately, with an explicit `--ignore-dependencies` escape hatch shipped in the same
change**, not a warn-only rollout — silently ignoring a still-depended-on provider is the exact defect
being fixed, and half-measures reproduce it.

**Both uninstall and install-time detection fail closed on an unreadable registry, worded distinctly from
a genuinely-missing/unsatisfied provider.** Concretely:

- `DependencyDetector.DetectBlockingDependencies` (uninstall side) returns `Result<IReadOnlyList
  <DependencyBlocker>>`. A registry read error on *either* `HKLM` or `HKCU` propagates as a `Failure`,
  which `PlanStep` turns into a hard refusal (`ErrorKind.PlanningError`). An unknown state must never be
  silently treated as "no dependants" — that collapse is the original defect class.
- `DetectUnsatisfiedProviders` (install side) returns `Result<IReadOnlyList<UnsatisfiedProviderInfo>>`,
  reading via `IRegistry.TryGetStringValue` (the `Result`-returning counterpart of `GetStringValue`,
  added alongside `TryReadSubKeyNames` for this purpose). A registry read error on either root propagates
  as a `Failure`, which `PlanStep` turns into a "Cannot verify dependency state safely" refusal — distinct
  from the "required dependency provider(s) not satisfied" refusal a genuinely missing/unsatisfied
  provider produces. This distinction matters: before `TryGetStringValue` existed, `GetStringValue` threw
  an uncaught exception on an access-denied read, which the pipeline's top-level exception handler turned
  into a generic, unworded `EngineError` crash — an accidental fail-closed outcome, not a deliberate one,
  and not what the original text of this ADR described. The read error is now a first-class, correctly
  worded refusal on both sides, not a caught-by-accident crash on one side and an assumed-impossible case
  on the other.
- The write side (`ApplyStep` registering/unregistering after a successful apply) is where "fail open"
  actually applies: a persistence failure there (registry access denied, elevated round-trip failure, no
  elevation companion available) is caught and never turns an already-successful install/uninstall into
  a reported failure. **The containment claim is direction-dependent, not universal — amended 2026-08-01
  after review found the original wording overstated it.** On INSTALL, a write failure is genuinely
  contained: nothing else was relying on the registration yet, a genuinely broken machine will fail
  loudly on its own at the next relevant operation, and this direction is logged as a Warning. On
  UNINSTALL, a write failure is NOT contained: this bundle's own consumer entry survives the failed
  unregister, potentially forever (nothing currently collects orphaned consumer entries — see the
  Consequences section), which can permanently strand a hard block on a completely unrelated THIRD
  product's future uninstall of the shared provider it referenced. Because that failure mode is
  materially worse and easy to miss, the uninstall direction is logged at Error and names the exact
  registry path(s) that need manual clearing, rather than being folded into the same Warning as install.

**Scope-mirrored write, union read.** A `PerUser` bundle writes its own provider/consumer registrations
directly to `HKCU` (no elevation). A `PerMachine` bundle writes to `HKLM` through the elevated
companion. Both the uninstall and install checks read **both** roots regardless of the current bundle's
own scope — a per-user consumer of a per-machine provider (or the reverse) is a real, supported shape,
and checking only the writer's own root would miss it.

**A separate elevated command, not a `RegistryWriteCommand` carve-out.** `RegistryWriteCommand`'s
allowlist permanently reserves `SOFTWARE\Classes\` (COM/shell hijack surface) — we did not un-reserve it
for this one path, which would reopen that surface for every other caller of the general-purpose write
command. Instead, `DependencyRegistrationCommand` is a new elevated command with its own allowlist
scoped to exactly `SOFTWARE\Classes\Installer\Dependencies\`, plus a traversal/injection check on every
provider/consumer key segment (those values originate from the manifest, which can be
attacker-authored) before they are interpolated into a registry path.

**Silent mode does not imply `--ignore-dependencies`.** They are orthogonal flags. Silent/unattended
uninstall is automation, which is exactly where an unexpected silent break of a dependent product would
hurt most — it must still be refused unless the override is explicit.

## Consequences

- An uninstall that previously always succeeded (because the check was never wired at all) can now be
  genuinely refused. This is a real behavior change for any existing `DependencyProvider`/
  `DependencyConsumer` bundle, documented in the beta.6 release notes as something to audit before
  upgrading.
- MSI-authored dependencies (via `FalkForge.Extensions.Dependency`'s table contributor) already exist on
  some machines from earlier releases; nothing added by this change retroactively cleans those up. A
  stale MSI-authored consumer entry can block a bundle uninstall exactly as a stale bundle-authored one
  can. `--ignore-dependencies` is the immediate escape hatch; nothing currently collects an orphaned
  provider row once its last consumer is gone (the `BundleId` value stamped into each consumer subkey by
  `DependencyRegistrar` exists specifically so a future orphan-collection pass has something to key off
  — that pass is not built yet).
- `DependencyRegistrationCommand` widens the elevated companion's SYSTEM-privileged attack surface by
  one command. Its allowlist is narrower than `RegistryWriteCommand`'s (a single fixed prefix, not a
  general `SOFTWARE\<AppName>\` pattern), and `ElevatedHostCommandSurfaceTests` pins the exact registered
  command set so a future addition to that surface is a reviewed, deliberate decision.
- The "fail open on a write-side persistence failure" choice means a per-machine install whose elevated
  registration round-trip silently fails only produces a log Warning — an operator who doesn't read logs
  will not see the omission. This mirrors the existing pattern for other best-effort elevated writes in
  the codebase (e.g. the C16 trust-store advance) rather than introducing a new failure philosophy.
- Reversing the fail-closed/fail-open split (e.g. making a write-side failure hard-fail the apply, or
  making the uninstall check fail open on a read error) would need a new ADR — this one is the record of
  why the asymmetry is deliberate, not an oversight.
- **A stale provider row can make a MISSING dependency read as satisfied (added 2026-08-01).** A
  provider's own uninstall removes only its `DependencyConsumers` entries — its own provider row (the one
  `DetectUnsatisfiedProviders` reads `Version` from) is never removed, deliberately: the same provider key
  can legitimately be written by more than one product (an MSI-authored dependency and a bundle-authored
  one sharing a key, or several products from one vendor), so there is no way to tell "the last provider
  of this key just uninstalled" from "another one is still present" without actually reference-counting
  providers the way consumers already are — a mechanism that does not exist today. Considered and
  rejected: unconditionally deleting the provider row on its own uninstall, which would be simpler but
  would incorrectly un-satisfy the requirement for every OTHER product still legitimately providing the
  same key. The accepted trade-off leaves a narrow but real gap in the other direction from the
  stale-refusal case documented above: after the actual provider is gone, `DetectUnsatisfiedProviders` can
  read the leftover row and let a dependent install proceed against a component that is no longer present.
  `documentation.html` §9.3e and this file previously disclosed only the stale-BLOCKER direction; both now
  disclose this stale-SATISFIED direction too, since "install refusal on missing dependency" is a weaker
  guarantee than release notes describing it have implied.
- **An elevated bundle can Register a dependent under a provider key it does not own (added 2026-08-01).**
  `DependencyRegistrationCommand`'s allowlist and traversal guard constrain WHERE in the registry a
  payload can write, not WHOSE provider key it claims to depend on — nothing stops bundle A's manifest
  from declaring `DependencyConsumer("SomeOtherVendorsProviderKey", "BundleA")`. Once registered, that
  consumer entry legitimately (per this ADR's own reference-counting design) blocks a future uninstall of
  the other vendor's component until BundleA's entry is removed — an effective cross-vendor lockout. This
  is contained by the facts that (a) the write is confined to the fixed
  `SOFTWARE\Classes\Installer\Dependencies\` prefix (the allowlist's whole purpose) and (b) it requires the
  same elevation consent any per-machine install already needs — it is not a privilege-escalation path,
  only a mis-scoped-trust one. `--ignore-dependencies` remains the operator's escape hatch if this occurs.
