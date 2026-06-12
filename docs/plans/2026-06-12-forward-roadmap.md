# FalkForge Forward Roadmap — The Provable Installer (2026-06-12)

Status: DRAFT. Owner: Peter.
Inputs: Gap audit (code) 2026-06-10, plans inventory (73 docs), quality assessment 9.2/10 (2026-06-10).
Effort scale: S = ≤1 day, M = 2-5 days, L = 1-3 weeks, XL = >3 weeks (solo, TDD gates included).

## 1. Context

FalkForge is technically excellent and strategically undecided. 6,380 tests, CI green, all six architecture-deepening RFCs shipped, reproducible PackageCode just landed. Meanwhile: the auto-updater is 80% built but never constructed in a live session, the plan exporter is complete but unwired, the supply-chain cluster (SBOM, Sigil.Sign, dry-run export) is designed but unshipped, and the one CRITICAL finding is in the update path. The pattern across the audits is consistent: **FalkForge keeps building trust infrastructure and not turning it on.** This roadmap turns it on, in an order where each phase makes the next phase's story stronger.

## 2. Thesis: The Provable Installer

In 12 months FalkForge should be **the only Windows installer toolchain where every claim is verifiable**:

- **Prove what you shipped** — reproducible builds with content-derived PackageCode (shipped), SBOM sidecar, signed payloads with runtime verification, SLSA-style provenance.
- **Prove what will happen** — `forge plan` exports the exact install plan before a single byte is written; plan diffs between versions become CI artifacts; ICE validation actually runs.
- **Prove what happened** — rollback journal, per-session logs, decompiler that round-trips any MSI/bundle (including WiX's) back to readable C#.
- **Prove updates are authentic** — Authenticode + ECDSA-verified update chain, delta updates with SHA-256 verification at every hop.

No competitor tells this story. WiX has none of it (XML in, opaque MSI out, Burn updates are DIY). Advanced Installer is closed-source GUI — "trust us" is the opposite of provable. Squirrel/Velopack do updates but not MSI/enterprise. The story also subsumes FalkForge's other strengths rather than competing with them: code-first is *why* builds are reproducible; the decompiler is *how* you audit; the 3-process NativeAOT engine is *why* plan/apply can be separated and verified.

Secondary thread (Phase 3): the **switching funnel**. The decompiler already converts WiX Burn v3/v4 bundles and arbitrary MSIs to C#. Productized as `forge migrate`, that is the single highest-leverage adoption move available — "port your WiX installer in an afternoon, and get a provable build pipeline for free."

Why not "modern .NET-native installer" as the thesis? Too generic; it describes the implementation, not the buyer's reason. Provability is the reason a team *switches*; .NET-native is the reason they *stay*.

## 3. Kill List

| Item | Verdict | Reason |
|---|---|---|
| G2 Custom bootstrapper application hosting (XL) | **Kill** | FalkForge's engine+UI *is* the BA replacement. Hosting third-party BAs is WiX-compat busywork that contradicts the 3-process NativeAOT model and helps people not switch. |
| C1 Runtime plugin discovery + C2 plugin sandbox (XL+XL) | **Kill, reframe** | NativeAOT cannot load assemblies at runtime; fighting that is a year of work for a worse security posture. Reframe the constraint as a feature: plugins are compile-time NuGet packages, statically linked, covered by the SBOM — *no runtime DLL loading is a supply-chain claim*. Document this as the official ecosystem model. |
| WiX parity round 6 | **Kill after parity-final-three** | Re-analyzed 5× with diminishing returns. Ship the three already-designed items real migrators hit (HTTP package sources, feature state migration, exit-code mapping) in Phase 3, then declare parity frozen; further gaps handled on user demand only. |
| MSIX deep investment | **Freeze at experimental** | Ship A2 (CLI dispatch, S) so the existing compiler is reachable, then stop. WinGet manifests already cover modern distribution; MSIX demand signal is absent from the audits. |
| I2 Driver KMCS/WHQL workflow | **Kill (document instead)** | External Partner Center dependency, tiny audience; a docs page beats a feature. |
| Studio: VS extension, repackager (EXE→MSI capture), VM testing from Studio | **Kill / indefinite defer** | Each is a separate product. Repackager especially — snapshot/capture engines are XL with endless edge cases. Studio's roadmap doc grows every revision; Studio's job in this plan is exactly two things: live validation and a face for the migration wizard. |
| License keys, COM+, instance transforms, multiple-instance, streaming install, admin installs | **Stay deferred** | No audit evidence of demand; none feed the thesis. |
| HSM/OIDC keyless signing, full transparency log infra | **Defer to Phase 4, scoped down** | Signed update feed (TUF-lite, see §5) captures 80% of the value at 10% of the cost. |

## 4. Phased Roadmap

### Phase 1 — Trustworthy by Default (≈4-6 weeks)

*Theme: close the gap between what the trust story claims and what the code does. Everything here is fixing wiring, not building new systems — the parts exist.*

| # | Item | Evidence | Effort |
|---|---|---|---|
| 1.1 | **B1 (CRITICAL): Authenticode-verify downloaded updates before launch.** `DefaultUpdateLauncher.Launch` (`src/FalkForge.Engine/UpdateLauncher.cs:36`) calls `Process.Start` raw; inject `IAuthenticodeValidator` (exists in Platform.Windows, already used by PackageCache) and refuse unsigned/invalid binaries. Add optional pinned-publisher check sourced from the manifest. | Verified in code | S-M |
| 1.2 | **A3+A4+B3: finish the auto-update loop.** Construct UpdateChecker/UpdateDownloader/DeltaApplicator in the live session, replace the log-and-ignore branch at `PipelineRunner.cs:151-154` with real LaunchUpdate dispatch, add post-update process handoff (exit after launch). The 854-LOC plan exists; infra is 80% done. Demo 42 becomes a true end-to-end demo. | Audit A3/A4/B3 + auto-updater UX plan | M-L |
| 1.3 | **A1: wire `--plan-only` in Engine.** Flag is parsed then discarded in `Engine/Program.cs`; `PlanExporter` and the CLI `PlanCommand` are complete. This is the cheapest USP unlock in the repo. | Verified in code | S |
| 1.4 | **E1: ICE validation must fail loud in CI.** darice.cub absent on the runner means MSIs ship unchecked today. Make absence an error (with explicit opt-out), provision the cub on the runner, and ship `forge validate --ice` + suppressions + JSON report per the 2026-03-06 design. | Audit E1 + ice-validation-ux plan | M |
| 1.5 | **Protocol versioning policy.** Wire format already carries `[Version:u16]`; write and enforce the rule now: additive-only payload fields per version, bump criteria, reject/downgrade behavior for unknown versions, one test per codec asserting wire stability. Cheap now, catastrophic to retrofit after engine and UI ship from different releases (which Phase 1's updater makes routine). | Plans-inventory theme 3 | S-M |
| 1.6 | **Verification sweep:** confirm security-memory-perf S1-S4 status (unknown per inventory); add an exhaustiveness test for `FeatureBuilder.CollectFiles` (hand-copies 11 `FileEntryModel` properties — any future field silently drops; original reported bug appears fixed but is untested against new fields). | Inventory postscript, verified in code | S |

**Compounding logic:** Phase 1 makes the update path *actually secure* before Phase 2 advertises it, and `--plan-only` wiring is the substrate every Phase 2 provability feature builds on. Shipping an "update security" claim while B1 is open would be indefensible.

### Phase 2 — The Provable Pipeline (≈6-8 weeks)

*Theme: ship the designed-but-unshipped USP cluster and back it with adversarial testing. This is the phase that creates the story no competitor has.*

| # | Item | Evidence | Effort |
|---|---|---|---|
| 2.1 | **Sigil.Sign payload signing + runtime ECDSA verification.** Design complete (2026-03-16). Bundles verify payload signatures before execution, independent of Authenticode. | Sigil integrity plan | L |
| 2.2 | **Supply-chain phase 1 completion:** SBOM sidecar finished (partially shipped), dry-run/plan export integrated with 1.3, provenance documentation. | Supply-chain plan | M |
| 2.3 | **`forge verify --rebuild`** *(new idea, §5.1)*: rebuild from source and byte-compare against a shipped artifact. Reproducible mode + content-derived PackageCode (shipped 2026-06-09) make this possible *today*; this command is the proof ceremony. | New | M |
| 2.4 | **`forge plan diff`** *(new idea, §5.2)*: human-readable plan diff between two artifacts, designed as a CI/PR artifact. | New | M |
| 2.5 | **H4: fuzz the untrusted-input parsers** — ConditionEvaluator, MessageDeserializer, CabinetExtractor, WixBurnAccess. A provability story invites adversaries; nightly fuzz CI is the table stakes that make the claim honest. | Audit H4 | M-L |
| 2.6 | **SLSA-style provenance via GitHub attestations** for FalkForge's own releases — eat the dog food, publish the attestation, link it from the README. | Deferred-harvest item, scoped down | S |

**Compounding logic:** 2.1+2.2 are the marketing claims; 2.3+2.4 are the demos that make the launch land; 2.5+2.6 are the credibility floor. Phase 2 also produces the launch content (blog post: "An installer you can independently rebuild and diff") that Phase 3's migration funnel converts.

### Phase 3 — The Switching Story (≈8-10 weeks)

*Theme: remove every reason a WiX/Inno user bounces during their first afternoon.*

| # | Item | Evidence | Effort |
|---|---|---|---|
| 3.1 | **`forge migrate`** *(new packaging of existing tech)*: wrap MsiDecompiler/WixBundleDecompiler into a one-shot project generator — emit csproj + SDK reference + organized C# + payload extraction + a "what didn't map" report (WixUnmappedFeature exists). The decompiler is the moat; today it emits code, not a project. | Decompiler + Studio-roadmap migration wizard, descoped to CLI-first | L |
| 3.2 | **G1: Burn `<Search>` equivalents** — file/registry/product searches driving bundle install conditions. The most common real-world Burn feature a migrator hits with no FalkForge answer. | Audit G1 | L |
| 3.3 | **Bundle variables (typed/persisted/secret).** Design ready; also retires the secrets-on-command-line concern that recurs 4× across plan docs. | Unshipped design | M-L |
| 3.4 | **Per-MSI progress via MsiSetExternalUIW.** Design ready. Smooth progress is the single most *visible* quality signal in a demo video. | Unshipped design | M |
| 3.5 | **WiX parity final three** (HTTP package sources, feature state migration on upgrade, EXE exit-code mapping), then parity freeze per kill list. | Parity stream | M |
| 3.6 | **A5: Icon table producer** — migrated installers currently get null shortcut icons; a first-impression bug for every migrator. | Audit A5 | S-M |
| 3.7 | **J1: AutomationProperties baseline for installer UI.** Screen-reader-blind UI is an enterprise procurement blocker; enterprises are exactly who the provability story attracts. Full WCAG 2.2 AA deferred; baseline now. | Audit J1 | M |
| 3.8 | **Studio phase 2 (live validation) + migration wizard front-end** over 3.1. Nothing else from the Studio roadmap. | Studio phase 2 design | L |

**Compounding logic:** Phase 2's launch content generates WiX-user attention; Phase 3 converts it. Sequencing 3.1 before 3.2/3.5 would strand migrators on missing features — but doing 3.2/3.5 without the funnel wastes them. They ship together as one release.

### Phase 4 — Fleet & Polish (ongoing, demand-driven)

| # | Item | Evidence | Effort |
|---|---|---|---|
| 4.1 | `forge test --sandbox` *(new idea, §5.3)* — Windows Sandbox install-test harness. | New | L |
| 4.2 | `forge watch` *(new idea, §5.4)* — delta-rebuild inner loop. | New | M-L |
| 4.3 | Signed update feed, TUF-lite *(new idea, §5.5)*. | New | M-L |
| 4.4 | OTel export + failure log shipping + analytics hook — one coherent "install telemetry" feature for ISVs, opt-in, privacy-documented. | Audits D1/D2/I7 | L |
| 4.5 | Locale expansion — publish the 48-key JSON format and solicit community locale PRs; cheapest community-contribution surface in the repo. | Audit I6 | S + community |
| 4.6 | Studio dialog WYSIWYG — only if Phase 3 funnel metrics prove Studio demand. | Studio roadmap, gated | XL |
| 4.7 | MSP round-trip validation, PerUser-elevation edge, REINSTALLMODE — batch as a robustness sprint. | Audits I3/I5/I4 | M |

## 5. New Ideas (not in the audits)

1. **`forge verify --rebuild` — independently verifiable installers.** Given a source ref + a shipped MSI/bundle, rebuild in reproducible mode and byte-compare. Reproducible PackageCode (issue #1, shipped) was the last blocker. No installer tool on any platform offers third-party rebuild verification; this is the flagship demo and it is mostly *glue*.
2. **`forge plan diff` — install-plan diffs as PR artifacts.** Compare two artifacts (via PlanExporter + decompiler) and emit "v2.3 adds 1 service, 2 firewall rules, writes HKLM\X" as markdown for CI bots. Turns the security team from an installer adversary into the feature's biggest fan. Variant: run Detect+Plan against the *current machine* ("what would this do to THIS box") — the 3-process split makes plan-without-apply trivial; msiexec fundamentally cannot do this with rich output.
3. **`forge test --sandbox` — installer tests in Windows Sandbox.** Launch the bundle in Windows Sandbox (free, built into Win10/11), apply, capture the rollback journal + before/after machine diff, assert from xUnit via the Testing project. Installer testing is universally manual and dreaded; nobody ships this. Pairs naturally with the 87K-LOC test culture.
4. **`forge watch` — hot reload for installers.** File change → recipe-pipeline incremental rebuild → Octodiff delta bundle in milliseconds → auto-reapply to sandbox/test VM. Delta infrastructure (DeltaBundleCompiler, DeltaApplicator) already exists; pointing it at the dev inner loop instead of production updates is a reuse, not a build.
5. **Signed update feed (TUF-lite).** Feed JSON gets an ECDSA countersignature (Sigil key) with monotonic version + expiry; engine verifies feed signature before trusting any entry. Defends against compromised CDN/mirror — the attack the Authenticode fix doesn't cover. Completes the update chain: signed feed → signed delta → Authenticode-verified launch.

## 6. Quick-Wins Appendix (batch in one S-sprint, slot between phases)

- A1 Engine `--plan-only` wiring (also Phase 1.3 — do first)
- A2 MSIX CLI dispatch (then MSIX freeze)
- Coverage gate in CI
- `AnalysisLevel=latest-all`
- Benchmark baseline (BenchmarkDotNet on MsiCompiler + MessageSerializer hot paths)
- REINSTALLMODE knob
- RTL flag plumbing (layout-only, not full RTL audit)
- GitHub attestation on releases (2.6)
- FeatureBuilder.CollectFiles exhaustiveness test (Phase 1.6)
- Kill-list documentation: ecosystem model page ("compile-time plugins by design"), KMCS/WHQL how-to page

## 7. Success Metrics

| Phase | Metrics |
|---|---|
| 1 | Zero unverified update launches possible (test-enforced); demo 42 auto-update runs end-to-end incl. relaunch; CI fails when ICE cannot run; protocol version policy doc + per-codec wire-stability tests merged; mutation/coverage gates unchanged or better. |
| 2 | `forge verify --rebuild` green across all 53 demos; signed-payload verification on by default for new bundles; nightly fuzz job running with zero open crashes; FalkForge's own release carries a published attestation; launch post shipped. |
| 3 | `forge migrate` produces a *building* project from ≥3 real-world WiX installers (incl. one Burn bundle); time-from-`forge migrate`-to-first-successful-build < 30 min on a real project; shortcut icons + smooth per-MSI progress visible in demo video; accessibility smoke test (Narrator walk-through) passes on default UI. |
| 4 | `forge test --sandbox` used by FalkForge's own integration suite (dogfood); ≥2 community locale PRs; telemetry feature opt-in documented; Studio WYSIWYG go/no-go decided from funnel data, not enthusiasm. |

## 8. Risks

- **Solo bandwidth vs. four phases:** phases are sequential releases, not a backlog; each is independently shippable and the thesis survives stopping after Phase 2.
- **Provability claims invite scrutiny:** that is the point — fuzzing (2.5) and dogfooding (2.6) are scheduled *before* the loud marketing, not after.
- **Migration funnel may surface parity gaps beyond the final three:** triage against user evidence only; the parity freeze is a default, not a dogma.

## 9. Key Implementation Entry Points

- `src/FalkForge.Engine/UpdateLauncher.cs` — B1 fix (inject IAuthenticodeValidator before Process.Start)
- `src/FalkForge.Engine/Pipeline/PipelineRunner.cs:151-154` — LaunchUpdate ignored; updater session wiring
- `src/FalkForge.Engine/Program.cs` — `--plan-only` parsed then discarded; wire to PlanExporter
- `src/FalkForge.Engine/Planning/PlanExporter.cs` — substrate for forge plan, plan diff, rebuild-verify
- `src/FalkForge.Core/Builders/FeatureBuilder.cs` — CollectFiles property-copy exhaustiveness hazard
