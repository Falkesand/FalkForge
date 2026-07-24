# Mutation Testing

Mutation testing measures test *effectiveness*, not just reach. Line/branch coverage
(see [`coverage-baseline.md`](coverage-baseline.md)) only tells you a line executed
during the test run — it says nothing about whether a test would actually fail if that
line's logic were wrong. Stryker.NET answers that question directly: it introduces a
small semantic change ("mutant") into the compiled code — flips a `>` to `>=`, deletes
a null check, negates a boolean, swaps `&&` for `||` — then reruns the tests. If every
test still passes, the mutant *survived*: something in that code path has no test
actually pinning its behavior, even if coverage tooling shows the line as "covered".

A **mutation score** is the percentage of mutants killed (caused a test failure) out of
all mutants that were actually exercised by some test. It is a much stronger signal than
line coverage that the test suite encodes real behavioral guarantees.

## How to run it

```powershell
pwsh scripts/mutation.ps1 -TestProject <Name>.Tests -SourceProject <Name>
```

Example:

```powershell
pwsh scripts/mutation.ps1 -TestProject FalkForge.Signing.SignServer.Tests -SourceProject FalkForge.Signing.SignServer
```

`-TestProject` is the folder name under `tests/`. `-SourceProject` is the `.csproj`
name (without extension) being mutated. Optional: `-CoverageAnalysis` (default
`perTest`), `-Concurrency` (default `12`), `-Output` (default
`artifacts/mutation/<SourceProject>`, already covered by the repo's `artifacts/`
`.gitignore` entry).

The script requires the `dotnet-stryker` global tool
(`dotnet tool install -g dotnet-stryker`).

## The two traps that make this not "just run Stryker"

**1. The `global.json` / Buildalyzer trap — read this before touching the script.**
This repo's `global.json` pins `sdk.version` `10.0.103` with `rollForward:
latestFeature`. Plain `dotnet` commands resolve that fine through roll-forward. Stryker
does not go through plain `dotnet` — it hosts MSBuild in-process via Buildalyzer, and
Buildalyzer's SDK resolution fails **silently** against a pinned-but-not-installed SDK
version. There is no MSBuild error at any verbosity; Stryker just reports "Analyzing 1
test project(s)" followed by "No project found" / "Failed to analyze project builds"
and creates zero mutants. This was confirmed by toggling `global.json` on and off
against an out-of-repo copy of a test project: a vanilla net10.0 project with no
`global.json` mutates fine every time; the same project fails to analyze the moment a
pinned `global.json` like this repo's is placed next to it. The fix, applied
automatically by `scripts/mutation.ps1`, is `--msbuild-path` pointed at the SDK that
`dotnet --version` actually resolves to from the repo root (currently 10.0.302) —
`C:\Program Files\dotnet\sdk\<resolved-version>\MSBuild.dll`. If you ever see the
"no project found" symptom again, check this first: run `dotnet --version` from the
repo root and confirm that exact SDK folder exists under `C:\Program Files\dotnet\sdk`.

**2. The test-runner trap.** All test projects here are xunit.v3 3.2.2 running on
Microsoft.Testing.Platform (MTP), not classic VSTest. Stryker's default `vstest`
runner is unreliable against xunit.v3 (open upstream issue,
[stryker-mutator/stryker-net#3117](https://github.com/stryker-mutator/stryker-net/issues/3117)).
Every wired `tests/*/stryker-config.json` sets `"test-runner": "mtp"` instead. Upstream
Stryker itself still labels the MTP runner **preview** (added in Stryker 4.13) — expect
a preview banner in the console output, and treat any run under it as directional, not
gospel.

`"coverage-analysis": "perTest"` is also set in every wired config and is worth
keeping: it lets Stryker skip mutants that no test executes at all (`NoCoverage`),
which meaningfully cuts the number of mutants that need a full test run. The honest
caveat: under the MTP runner, Stryker cannot yet filter down to the individual test
*cases* that cover a given mutant the way it can under classic VsTest — it can only
skip mutants with zero covering tests. So `perTest` here is coarser than the same
setting would be under `vstest`; it still helps, just less than advertised for VsTest.

`--coverage-analysis` is **not** a CLI flag (`dotnet-stryker` rejects it as an
unrecognized option) — it is config-file only, which is why it lives in each project's
`stryker-config.json` rather than being passed on the command line.

## These runs are periodic, not per-commit

Mutation testing here is **not** part of the commit pipeline or CI. A single project
run can take minutes even with `perTest` coverage analysis narrowing the mutant set,
and a full-repo sweep across every wired project is a multi-hour affair. Run it
periodically (e.g. as part of an assessment pass) against whichever project is under
active scrutiny, not on every push. **No mutation score threshold is enforced
anywhere** — not in this script, not in CI, not as a merge gate. The `thresholds`
block in each `stryker-config.json` only controls what `dotnet-stryker`'s own exit
code and reporters do with the number; nothing in this repo's build or CI consumes
that exit code.

## Reading the report

Each run writes to `artifacts/mutation/<SourceProject>/reports/` (the exact path is
echoed at the end of the script run):

- `mutation-report.html` — open in a browser; per-file, per-mutant view with source
  overlay showing exactly which mutants survived and why.
- `mutation-report.json` — the same data in the
  [mutation-testing-report schema](https://github.com/stryker-mutator/mutation-testing-elements),
  useful for programmatic diffing between runs.

The mutation score itself (killed / tested, survived, timeout, compile-error, ignored
counts) is printed directly to the console by Stryker's own `ClearText`/`Progress`
reporters during the run — this script does not re-derive or re-print it from the
report files, to avoid guessing at a schema it hasn't independently parsed.

## Results

Only verified, actually-executed runs are recorded here. Do not add a row for a
project that has not been run end-to-end with this script — an unverified number is
worse than no number.

**2026-07-24 sweep** — 5 trust-critical projects, Stryker.NET 4.16.0, MTP runner,
`coverage-analysis: perTest`, `concurrency: 12`, `mutation-level: Standard`. The
documented behaviors above (MTP runner being preview, `--coverage-analysis` being
config-file-only, the `--msbuild-path` workaround) were all verified against this
exact `dotnet-stryker` 4.16.0. A future contributor running against a newer Stryker
version should re-verify each of those before trusting them — none are guaranteed
to still hold upstream:

| Project | Score | Mutants created | Tested | Killed | Survived | Timeout | NoCoverage | Ignored | CompileError | Wall |
|---|---|---|---|---|---|---|---|---|---|---|
| FalkForge.Core | 80.17% | 4163 | 2831 | 2491 | 339 | 0 | 277 | 441 | 614 | 2m03s |
| FalkForge.Compiler.Bundle | 68.27% | 1691 | 1047 | 802 | 240 | 5 | 135 | 217 | 292 | 5m40s |
| FalkForge.Signing.SignServer | 64.96% | 165 | 114 | 88 | 25 | 1 | 23 | 15 | 13 | 0m16s |
| FalkForge.Engine.Protocol | 57.75% | 2269 | 1319 | 919 | 366 | 31 | 329 | 285 | 336 | 4m53s |
| FalkForge.Engine.Elevation | 48.37% | 628 | 294 | 237 | 56 | 1 | 198 | 94 | 42 | 0m41s |

Every number above was cross-checked by re-parsing each `mutation-report.json`
(counting mutant `status` values per project) rather than trusting the console
transcript — the JSON's per-mutant `status` field is the ground truth for every
column except `Wall`, which is console-only timing not present in the JSON schema
and is taken from the console output as-is. `Tested` is defined consistently across
every row in this table as `Killed + Survived + Timeout + RuntimeError`
(`NoCoverage` is deliberately excluded — those mutants were never reached by a test,
so they were not "tested").

Two cells needed a real correction against what had been recorded here before this
pass: **FalkForge.Signing.SignServer's `Survived` figure was originally 48**, but the
JSON shows only 25 true `Survived` mutants plus 23 separate `NoCoverage` mutants
(25 + 23 = 48) — the original 48 conflated the two categories because the console's
plain summary block does not print `NoCoverage` as its own line the way the other
four projects' runs happened to have logged it. That same conflation also corrupted
**`Tested`, originally recorded as 137** — that number is `Killed + Survived[48] +
Timeout + NoCoverage`, i.e. it double-counts the 23 `NoCoverage` mutants (once
folded into the wrong `Survived` figure, once added again directly) and uses a
different formula than the other four rows. Applying the same `Tested = Killed +
Survived + Timeout + RuntimeError` formula as every other row gives the correct
value: **114** (88 + 25 + 1 + 0). The 64.96% score itself was already correct and is
unchanged — Stryker's score formula includes `NoCoverage` in its own denominator
independently of the `Tested` column (see below), so the `Survived`/`Tested` bug
never touched the score math. The other four projects' Survived/NoCoverage/Tested
columns already matched the JSON exactly and needed no correction. `RuntimeError`
mutants (1 in Core, 3 in Protocol) exist in the raw data but have no column here —
they count toward `Tested` but are excluded from the score denominator, matching
Stryker's own formula `(Killed + Timeout) / (Killed + Survived + Timeout +
NoCoverage)`. Do not conflate the two formulas: `Tested` counts what tests actually
exercised; the score denominator additionally counts `NoCoverage` because an
unreached mutant still counts against the score.

Known caveats — read before trusting any single number in isolation:

- **NoCoverage counts against the score.** Stryker treats an uncovered mutant as
  "not killed," so a project with many execution paths no test reaches at all scores
  low even in files where the code that *is* tested is tested well. NoCoverage is a
  reach problem, not necessarily an assertion-quality problem — don't conflate it
  with Survived when reading the table.
- **`FalkForge.Core\Validation\CustomTableRules.cs` is effectively unmeasured.**
  Stryker hit `CS0165 Use of unassigned local variable 'strVal'` while compiling a
  mutant in this file, engaged its "Safe Mode," and discarded every mutation in the
  whole file as `CompileError` (273 of Core's 614 CompileError count comes from this
  one file alone — confirmed by re-parsing the JSON: 0 Killed, 0 Survived, 0
  NoCoverage, 0 Timeout, 0 Ignored, 273 CompileError for that file, score `N/A`).
  This file's real mutation resistance is unknown, not 100%.
- **The MTP runner is upstream-preview**, not just here — Stryker prints its own
  preview banner on every run regardless of project. Treat every score in this table
  as directional.
- **FalkForge.Engine.Protocol had 31 timeouts.** Timeouts count as killed in
  Stryker's score, so they inflate the score slightly versus a hypothetical run
  where the same mutants died from an assertion instead of a hang. 31 out of 2269
  mutants is not large, but it is worth watching if the number grows on a re-run —
  it can mean a mutant introduced an infinite loop / deadlock the suite happened to
  time out of rather than a test that actually pins the behavior.

### Notable surviving mutants

This is a findings record, not a fix list — none of these have been touched.
Selected from the files with the most `Survived` mutants (not merely `NoCoverage`)
across all 5 projects, favoring bundle/integrity/signing/elevation code over pure
MSI-table validation rules. Full per-mutant detail (mutator, exact location,
replacement text) lives in each run's `mutation-report.json`.

**FalkForge.Compiler.Bundle**

- `Compilation/BundleDetacher.cs` (74 survived, worst count of any file in the
  sweep) — the TOC `entryCount < 0 || entryCount > 100_000` crafted-bundle guard
  (line 58) survives both as a whole (`&&` swap) and at each individual boundary
  (`<= 0`, `>= 100_000`): no test feeds a bundle with `entryCount` of exactly `-1`,
  `0`, `100_000`, or `100_001` to pin the fence precisely. Same class of gap repeats
  at the PE certificate-table region-end boundary (`pos + 8 > regionEnd`, line 462).
- `Validation/BundleValidator.cs` (31 survived) — the hex-digit range checks in
  `IsValidSha1Thumbprint`/`IsValidSha256Hex` (lines 272, 292:
  `c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F')`) survive
  almost every boundary flip (`and`→`or`, `<`↔`>`) on every character class. No test
  supplies a thumbprint/public-key-pin string with a character just outside a valid
  hex range (e.g. `'g'`, `':'`, `'@'`) to prove the validator actually rejects it.
- `Compilation/BundleIntegritySigner.cs` (14 survived, worst score of the top
  offenders at 42.3%) — two whole-block removals survive intact: lines 180-188 (the
  entire SBOM component-population loop reduced to a no-op) and lines 158-160. The
  first means no test asserts the generated SBOM actually contains the payload
  component list — the loop could be deleted and nothing would notice. The
  `!SigilDetector.IsAvailable()` gate (line 130) and the `IsFailure` early-return
  checks (lines 140, 152) also survive negated, but that is lower severity: the
  feature is explicitly documented as "opportunistic, never fatal," so the test
  environment's fixed sigil-availability state naturally can't exercise both sides.

**FalkForge.Engine.Protocol**

- `Bundle/BundleReader.cs` (49 survived) — the runtime twin of BundleDetacher's TOC
  guard, same unpinned `entryCount` boundary (line 62). More urgent than the
  compiler-side copy because this is the code that actually runs against an
  untrusted bundle at install time. The negative-size / payload-cap / physical-file
  bound checks (lines 98-109), despite being explicitly commented as allocation-DoS
  defenses, have every boundary (`< 0` vs `<= 0`, `>` vs `>=`) survive too — no test
  supplies a TOC entry at exactly the cap or exactly the file length.
- `Integrity/SignedPayloadTocVerifier.cs` (22 survived) — `!revokedFingerprints
  .Contains(signature.Fingerprint)` (line 63) survives negated. This is the
  signature-revocation filter itself; a negated check would silently invert it to
  "keep only revoked signatures." No test builds a real revoked-fingerprint set,
  runs it against a collected-signatures list containing a match, and asserts the
  match is dropped — the revocation control's core behavior is unproven by the
  suite. This is the most concrete finding in the sweep.
- `Integrity/IntegrityEnvelopeCodec.cs` (21 survived; also 130 CompileError in this
  one file — the highest CompileError count of any file below Core's
  CustomTableRules.cs, worth a look separately) — `hasEpochOrRevoked = epoch != 0 ||
  revoked is { Count: > 0 }` (line 87) survives both the `is not {Count: >0}` flip
  and the `>= 0` boundary. This flag decides whether the canonical signed bytes
  include the epoch/revocation extension at all; no test pins the
  epoch-zero-with-empty-revoked vs. epoch-zero-with-nonempty-revoked matrix, so a
  regression here could silently downgrade a revocation-carrying envelope to the
  legacy (v1) signed shape. The v1→v2 adapter guard (lines 297-299, three ANDed
  conditions) also survives with one conjunct dropped — no test constructs an
  envelope where exactly one of the three legacy-shape conditions is false.
- `Transport/PipeTransportBase.cs` (20 survived, plus 14 timeout — the highest
  timeout count of any file) — the send-side (`data.Length > MaxMessageSize`, line
  49) and receive-side (`messageLength <= 0 || messageLength > MaxMessageSize`, line
  96) message-size caps both survive at their exact boundary. No test sends a
  message of exactly `MaxMessageSize` bytes to prove the fence is where the code
  says it is.
- `Integrity/QuorumEvaluator.cs` (18 survived) — the bipartite-matching sentinel
  `Array.Fill(slotToSig, -1)` (line 55) survives mutated to `Array.Fill(..., +1)`.
  `+1` is a real, reachable signature index (not an obviously-invalid sentinel like
  `-1`), so this is not cosmetic: no test exercises a case where a slot is genuinely
  unmatched *and* signature index `1` exists, which would be needed to observe the
  sentinel collision. This is the highest-priority finding in Engine.Protocol next
  to the revocation-filter negate above, because a wrong sentinel could make the
  quorum matcher misreport a slot as matched when it isn't. The negative-count
  clamp `req.Count < 0 ? 0 : req.Count` (line 44) also survives with the clamp
  removed — no test builds a `PolicyRule` with a negative `Count` requirement.

**FalkForge.Signing.SignServer**

- `EcdsaSignatureFormatConverter.cs` (9 survived) — the DER-vs-P1363 format sniff
  `signature.Length >= 2 && signature[0] == 0x30` (line 32) survives with the `&&`
  weakened to `||` and the `>= 2` narrowed to `> 2`. No test supplies a 1-byte or
  exactly-2-byte signature starting with `0x30` to prove the length guard runs
  before the `signature[1]` access it protects — in a file whose entire job is
  resolving signature-format ambiguity, the ambiguous short-buffer case itself is
  untested.
- `SignServerSignatureProvider.cs` (12 survived) — the mTLS wiring guard
  `config.AuthMode == SignServerAuthMode.ClientCert && config.ClientCertificate is
  not null` (line 180) survives with the `&&` weakened to `||`. No test constructs
  `AuthMode = ClientCert` with a null certificate, or a non-ClientCert mode carrying
  a leftover certificate object, to prove the AND is load-bearing — worth a look
  since an auth-mode/certificate mismatch is exactly the kind of confusion `Result<T>`
  strong typing is supposed to prevent elsewhere in this codebase.

**FalkForge.Engine.Elevation**

- `Commands/MsiInstallCommand.cs` (11 survived) — `ValidateAdditionalArgs`'s own doc
  comment calls it an injection guard ("A forged or misused peer must not be able to
  inject an extra MSI property"), which makes its survivors the most concrete
  finding in this project. `value.IndexOfAny(prohibited) >= 0` (line 150) survives
  narrowed to `> 0`: a prohibited character at index 0 (the very first character of
  the value) would no longer be rejected under the mutant, and no test supplies a
  malicious value starting with a prohibited character to catch that. The embedded-
  quote-smuggle guard (line 136, `i + 1 >= span.Length || span[i] != '=' ||
  span[i + 1] != '"'`) has its own off-by-one arithmetic (`i - 1`) and boundary
  (`i + 1 > span.Length`) survive similarly — the value-ends-exactly-at-buffer-end
  edge case is unpinned.

### How to re-run

Re-run any of the above with `scripts/mutation.ps1` (see "How to run it" above) —
e.g. `pwsh scripts/mutation.ps1 -TestProject FalkForge.Engine.Protocol.Tests
-SourceProject FalkForge.Engine.Protocol`. The **tables and findings in this
document are the durable record** — 4 of the 5 JSON reports behind this sweep lived
in a temporary scratchpad directory that does not persist, and `artifacts/mutation/`
is gitignored, so none of the original `mutation-report.json` files backing this
sweep survive past the session that produced them. A re-run produces a fresh report;
compare its numbers against the tables/findings recorded here, not against the
original files (they are gone).

## Config set

`tests/*/stryker-config.json` files carry per-project settings (which `.csproj` to
mutate, `mutate` glob include/excludes, `additional-timeout` for slow suites). Every
config wired for real use follows the same shape: `project`, `test-runner: "mtp"`,
`coverage-analysis: "perTest"`, `concurrency: 12`, `mutation-level: "Standard"`,
`reporters: ["cleartext", "progress", "json", "html"]`, and a `thresholds` block
(`high: 80`, `low: 60`, `break: 0` — informational only, see above, nothing enforces
these). `FalkForge.Integration.Tests` deliberately sets `"mutate": []` and zeroed
thresholds: it tests across multiple assemblies rather than one 1:1 source project, so
it is intentionally excluded from mutation (there is no single project to point
Stryker at) rather than misconfigured.

**Proven vs. unproven configs.** 19 `stryker-config.json` files exist under `tests/`.
Only **5 have ever actually been run end-to-end**: `FalkForge.Core`,
`FalkForge.Compiler.Bundle`, `FalkForge.Signing.SignServer`,
`FalkForge.Engine.Protocol`, `FalkForge.Engine.Elevation` — those are the ones with
real results in the table above. `FalkForge.Integration.Tests` is intentionally
excluded (see above, not a mutation target). The remaining 13 configs share the same
config shape but have **never been executed** — they are untested config shape, not
verified results, and may fail the first time they are run (e.g. the same class of
issue this document already documents for the `global.json` / Buildalyzer trap).
