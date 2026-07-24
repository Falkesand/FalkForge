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

Each run writes to `artifacts/mutation/<SourceProject>/<timestamp>/reports/`
(the exact path is echoed at the end of the script run):

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

| Date | Project | Mutants created | Tested | Killed | Survived | Timeout | Compile error | Ignored | Mutation score |
|---|---|---|---|---|---|---|---|---|---|
| 2026-07-24 | FalkForge.Signing.SignServer | 165 | 137 | 88 | 48 | 1 | 13 | 15 | 64.96% |

Further project results will be appended to this table as sweeps complete — this is
the only verified row so far.

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
