# FalkForge

<!-- RULES_SYNCED_FROM_GLOBAL: 2026-06-09 -->

C# MSI/Bundle installer framework. Fluent API, MSI compiler via P/Invoke, NativeAOT bundle engine with WPF UI. Extensions: Firewall, IIS, SQL, .NET, Dependency, Util, Driver, Http. Output: MSI, MSM, MSP, MST, EXE bundle.

## Build & Test
```bash
dotnet build          # 0 warnings (TreatWarningsAsErrors)
dotnet test           # ~8000 tests, xUnit (Microsoft.Testing.Platform)
dotnet publish -c Release  # NativeAOT for Engine + Elevation
```
.NET 10, C# latest, nullable enabled, central package mgmt. SDK 10.0.103. Solution: `FalkForge.slnx` (37 src + 30 test projects).

## Where to look

- **Codebase map** — full project inventory, dependency graph, key patterns, per-project layout, namespaces: `docs/codebase-map.md`. Read it on demand (it is NOT auto-loaded); it can drift, so the source wins on conflict.
- **Architecture + API reference** — `documentation.html` (24 sections, searchable) IS the source of truth. Hand-authored, tracked in git; edit it directly, there is no generator (the old `docs/gen/` concat script was retired 2026-07-12 after its stale fragments silently deleted manual sections).
- **Cross-module relationships** — `graphify-out/` knowledge graph (see `## graphify` below).

## Communication Mode

Default to caveman mode for user-facing chat text (invoked via `Skill: caveman`). Caveman applies to chat responses, status updates, subagent prompts and subagent final reports, TodoWrite items, end-of-turn summaries. It does NOT apply to file content: source code, comments, commit messages, PR descriptions, docs, tests, XML doc, or CLAUDE.md edits — those stay normal English. Default intensity `full`; use `ultra` for long status dumps; use `lite` only if user pushes back on readability.

Subagent dispatches MUST include this propagation line:
> COMMUNICATION: Invoke the `caveman` skill before your final report. Return your final message in caveman `full` mode. File contents (code, commits, docs) stay in normal English — caveman applies only to chat text.

## HARD LIMITS — Non-Negotiable Gates

Violating any gate means STOP, undo, and redo correctly.

### GATE 1: TDD — Test Before Code, Always

No production code without a failing test first. Cycle:
1. **RED** — write failing test, run, confirm fail. Do NOT commit.
2. **GREEN** — minimal impl to pass. All tests green.
3. **COMMIT** — fast-lane pipeline (see Commit Sequence). Test commit (`test:`) first, then impl commit (`feat:`/`fix:`).
4. **REFACTOR** — clean up, all green, fast-lane pipeline, commit (`refactor:`).

If you wrote impl before test, delete it and start over. Never combine "write test + implement" into one plan step. Applies to bug fixes, features, behavior-changing refactors, new types/methods. Skips XAML-cosmetic, config, docs.

### GATE 2: Full Solution Build After Every File Edit

After every .cs or .xaml edit, `dotnet build <solution>.slnx`. Not the project — the solution.
- Fix failures IMMEDIATELY before touching another file
- Do not batch edits hoping they'll "work together"
- Zero warnings policy
- The build runs the full Roslyn analyzer set under `TreatWarningsAsErrors`, so a clean (zero-warning) build IS the diagnostics gate

### GATE 3: All Tests Must Pass Before Any Commit

`dotnet test <solution>.slnx` before every commit. No `--filter`, no exceptions.

### GATE 4: Code Review Before Every Merge

`superpowers:code-reviewer` with Opus 4.6 and Sonnet 4.6 on the full branch diff. Both approve. Runs once per branch at the Merge Gate, not per commit — per-commit review only guards the feature branch, which nothing ships from.

### GATE 5: Security & Quality Audit Before Every Merge

At the Merge Gate, in order: `roslynator` → `quickdup` → OWASP scan (injection, secrets, deserialization, SSRF) → perf review (heap allocs, N+1, blocking async, disposal, unbounded collections). All on the full branch diff.

### GATE 6: Memory Efficiency — Zero-Waste Code

Code must be readable AND allocation-efficient:
- `stackalloc` for small fixed buffers (<1KB) in sync methods
- `Span<T>`/`ReadOnlySpan<T>` for slicing/parsing instead of substrings
- `Memory<T>` when data crosses async boundaries
- `ArrayPool<T>.Shared` for temp arrays
- `StringBuilder` in loops, never `+=`
- `ValueTask<T>` for hot async paths completing synchronously
- `struct` records for small immutable value types
- Avoid LINQ in hot paths (allocates enumerators/delegates)
- Avoid closures in hot paths (captured variables allocate)
- `FrozenDictionary`/`FrozenSet` for read-only lookup tables

If optimization reduces readability, add a one-line comment explaining WHY.

## Working Principles

These are values, not gates. Apply judgment; surface tension instead of hiding it.

### Simplicity first
Minimum code that solves the stated problem. No speculative features, no abstractions for single-use code, no "while I'm here" cleanup. If a senior engineer would call it overcomplicated, simplify.

### Surgical changes
Touch only what the task requires. Don't reformat, rename, or "improve" adjacent code, comments, or imports. Match existing style even if you disagree — if a convention seems harmful, surface it instead of forking silently.

### Read before you write
Before adding code, read its exports, immediate callers, and the shared utilities it would touch. "Looks orthogonal" is dangerous. If you can't explain why surrounding code is structured the way it is, ask before changing it.

### Tests verify intent, not just behavior
A test must encode WHY the behavior matters, not just WHAT the code currently does. If the business rule changes and the test still passes, the test is wrong. Naming, arrange/act/assert clarity, and negative cases all serve intent.

### Surface conflicts, don't average them
When two patterns or two pieces of guidance contradict, pick one (more recent, more tested, or closer to the current task) and explain why. Flag the loser for cleanup. Never blend conflicting patterns into a third hybrid.

### Fail loud
"Completed" is wrong if any step was skipped. "Tests pass" is wrong if any were filtered, skipped, or marked inconclusive. Surface uncertainty, partial results, and silent fallbacks — never hide them in a success summary. If the pipeline (see Commit Sequence) couldn't run a step, say so explicitly and stop.

## Commit Sequence — Fast Lane (every commit; in order, restart from 1 on any failure)

1. `dotnet build <sln>.slnx` — zero errors/warnings
2. `dotnet test <sln>.slnx --logger trx` — all pass
3. Diagnostics — covered by step 1: a zero-warning build already runs every analyzer as an error (no separate MCP step)
4. `git commit`

## Merge Gate — Deep Lane (once per branch, before merge to main; restart on any failure)

1. `dotnet build` + `dotnet test` — re-verify green
2. Review full-branch blast radius — read callers of changed public symbols (manual; the `csharp-roslyn` MCP was removed as low-value)
3. `jb inspectcode` if XAML changed — clean
4. `roslynator` — clean
5. `quickdup` — no new duplication
6. `superpowers:code-reviewer` Opus 4.6 on full branch diff — approved
7. `superpowers:code-reviewer` Sonnet 4.6 on full branch diff — approved
8. Security audit (OWASP checklist) + perf review on full branch diff — no findings
9. Merge to main — only after ALL above pass

Single-commit hotfix branches still pass the full Merge Gate — it is per-branch, not per-commit-count.

## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

Rules:
- ALWAYS read graphify-out/GRAPH_REPORT.md before reading any source files, running grep/glob searches, or answering codebase questions. The graph is your primary map of the codebase.
- IF graphify-out/wiki/index.md EXISTS, navigate it instead of reading raw files
- For cross-module "how does X relate to Y" questions, prefer `graphify query "<question>"`, `graphify path "<A>" "<B>"`, or `graphify explain "<concept>"` over grep — these traverse the graph's EXTRACTED + INFERRED edges instead of scanning files
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).
