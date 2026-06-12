# Fuzz Harness

**Added:** 2026-06-12 | **Scope:** Parsers that handle attacker-influenceable input

## What is fuzzed

FalkForge contains four parser components that process bytes from outside the trust boundary — MSI condition strings from installer packages, binary pipe frames from the UI process, cabinet archives, and FALKBUNDLE byte streams. Each parser has a deterministic in-suite fuzz harness and participates in the nightly deep-fuzz workflow.

| Parser | Location | Test project | Filter |
|--------|----------|--------------|--------|
| `ConditionLexer` + `ConditionEvaluator` | `src/FalkForge.Engine/Variables/` | `FalkForge.Engine.Tests` | `ConditionParserFuzz` |
| `MessageDeserializer` | `src/FalkForge.Engine.Protocol/Serialization/` | `FalkForge.Engine.Protocol.Tests` | `MessageDeserializerFuzz` |
| `CabinetExtractor` | `src/FalkForge.Compiler.Msi/` | `FalkForge.Compiler.Msi.Tests` | `CabinetExtractorFuzz` |
| `BundleAccess` + `BundleReader` | `src/FalkForge.Decompiler/` + `src/FalkForge.Engine.Protocol/Bundle/` | `FalkForge.Decompiler.Tests` | `BundleReaderFuzz` |

## Invariants

For every parser, every fuzz input must satisfy all of the following:

1. **No unhandled exception.** Every input returns `Result.Success` or `Result.Failure`. Unhandled exceptions are reported as test failures.
2. **No unbounded allocation.** Length fields (payload length, manifest length, TOC entry count) are capped by explicit guards before any allocation call. Crafted inputs claiming multi-gigabyte buffers are rejected with a typed failure.
3. **No hang.** Input size is bounded; native interop paths (FDI cabinet extraction) use modest iteration counts to keep the per-commit suite fast.
4. **Valid inputs succeed.** Each harness includes a round-trip sanity test against a known-good baseline to verify fuzz setup does not break the happy path.

## Pre-fuzz audit findings

Pre-fuzz inspection of all four parsers was performed before writing the harnesses. One bug was found:

**`BundleAccess.ReadManifest` — uncapped manifest length (fixed, BDC003)**

`BundleAccess.ReadManifest` read `manifestLength = _reader.ReadInt32()` then called `_reader.ReadBytes(manifestLength)` with no bounds check. A crafted FALKBUNDLE could declare `manifestLength = Int32.MaxValue`, causing a ~2 GiB heap allocation attempt before any bytes were read. The `OutOfMemoryException` was caught by the outer handler and returned as `Result.Failure`, so the process did not die — but the allocation attempt is a denial-of-service vector (GC pressure, extended pause).

**Fix:** Added a `maxManifestBytes = 64 MiB` cap. Negative or over-limit values return `Result.Failure(BDC003)` immediately without allocating.

All other parsers had pre-existing guards:
- `MessageDeserializer`: `MaxPayloadSize = 1 MiB` cap on payload length.
- `BundleReader` (Engine.Protocol): `entryCount > 100_000` guard + `manifestLen < 10 MB` guard.
- `BundleAccess.ReadToc`: `entryCount > 10000` guard.
- `WixBurnAccess`: `MaxUxContainerSize = 256 MB` guard.
- `ConditionLexer/Evaluator`: String-based; no raw integer-to-allocation conversion.

## Running the fuzz harnesses

### In CI (per-commit, ~300–400 iterations, fast)

The fuzz tests run as part of the normal test suite. No special steps needed:

```
dotnet test FalkForge.slnx --blame-hang-timeout 30s
```

### Scaling up locally

Set `FALKFORGE_FUZZ_ITERATIONS` before running to increase iteration counts:

```powershell
$env:FALKFORGE_FUZZ_ITERATIONS = 10000
dotnet test tests/FalkForge.Engine.Tests --filter "ConditionParserFuzz" --blame-hang-timeout 120s
```

### Nightly deep fuzz

The workflow `.github/workflows/nightly-fuzz.yml` runs every night at 02:00 UTC with `FALKFORGE_FUZZ_ITERATIONS=50000`. It runs each parser in a separate matrix job, time-boxed to 30 minutes. Failure artifacts are uploaded and retained for 30 days.

To trigger manually with a custom iteration count:

1. Go to **Actions → Nightly Fuzz** in the GitHub repository.
2. Click **Run workflow**, enter the iteration count, and confirm.

## Reproducing a CI failure

Every failing assertion embeds a fixed seed value, iteration index, and the first 32 hex bytes of the failing input in the assertion message. To reproduce locally:

1. **Find the seed and hex** in the TRX report under `<Message>` for the failed test case, or in the GitHub Actions log.
2. **Reconstruct the input:**
   - For `MessageDeserializer`: `MessageDeserializer.Deserialize(Convert.FromHexString(hex))`
   - For `BundleAccess`/`BundleReader`: write the hex bytes to a temp file and call `BundleAccess.Open(path)`
   - For `CabinetExtractor`: `CabinetExtractor.Extract(new MemoryStream(Convert.FromHexString(hex)))`
   - For `ConditionEvaluator`: `ConditionEvaluator.Evaluate(input, new VariableStore())`
3. **Re-run the same mutation sequence** using the seed and iteration: `var rng = new Random(seed); for (var i = 0; i < iteration; i++) { /* advance rng by the same mutation steps */ }`. Each harness uses a deterministic mutation sequence, so the same seed always produces the same inputs.
4. **Pin as a regression test** once reproduced: minimize the input to the smallest triggering case and add it as a `[Theory] [InlineData(...)]` test in the appropriate fuzz file.

## CabinetExtractor cautions

`CabinetExtractor` uses FDI (Windows File Decompression Interface) via P/Invoke. FDI handles malformed cabinet bytes internally; the extractor converts FDI failure codes to `Result.Failure`. However, native crashes inside FDI (stack corruption from extremely malformed input) would kill the test process rather than throwing a managed exception. The blame-hang timeout detects a killed test process and reports it as a test failure.

- **Iteration count is capped at 2000** for the cabinet fuzz tests even in nightly mode. FDI creates and deletes a temp file per call, which is slow.
- Cabinet fuzz tests are **Windows-only** (`[SupportedOSPlatform("windows")]`) because FDI is a Windows API.
- If a fuzz iteration kills the process, the TRX report will show a test timeout rather than an assertion failure. Check the blame dump file uploaded with the artifact.
