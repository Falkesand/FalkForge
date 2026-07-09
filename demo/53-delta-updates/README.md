# Demo 53: Delta Bundle Updates

## What This Demonstrates

Delta bundles contain only the binary differences between two versions of a bundle.
Instead of downloading a full 50 MB bundle, users download a 2 MB delta patch.

## How to Run

```bash
dotnet run --project demo/53-delta-updates -- -o ./output
```

## The Workflow

### 1. Build the base (v1) bundle normally

```csharp
var v1Bundle = new BundleBuilder()
    .Name("MyApp")
    .Version("1.0.0")
    .Chain(chain => { /* v1 packages */ })
    .Build();

new BundleCompiler().Compile(v1Bundle, outputPath);
```

### 2. Build the delta (v2) bundle referencing v1

```csharp
var v2Bundle = new BundleBuilder()
    .Name("MyApp")
    .Version("2.0.0")
    .DeltaFrom(v1Path)
    .Chain(chain => { /* v2 packages */ })
    .Build();

new DeltaBundleCompiler().Compile(v2Bundle, outputPath, v1Path);
```

### 3. Publish both URLs in your update feed

```json
{
    "version": "2.0.0",
    "url": "https://releases.example.com/myapp-2.0.0.exe",
    "deltaUrl": "https://releases.example.com/myapp-2.0.0-delta.exe",
    "deltaSha256": "...",
    "deltaSize": 2048000
}
```

### 4. Engine handles the rest

Delta handling is split across download time and install (relaunch) time:

1. `UpdateDownloader` checks the update feed and downloads the delta bundle (small), verifying its SHA-256.
2. It relaunches the downloaded delta bundle, passing the currently-installed (base) bundle as `--base-bundle` -- this is exactly the base the delta was built against.
3. The relaunched bundle reconstructs each delta payload with `DeltaApplicator`: it locates the matching payload in the base bundle, checks the base payload's hash equals the delta's `BaseSha256Hash`, applies the Octodiff delta, and verifies the finished payload against `ReconstructedSha256Hash` before writing it.
4. Any failure (wrong/missing base, hash mismatch, apply error) writes no output and fails loudly.

A failed delta *download* falls back to the full bundle URL. A delta that downloads but cannot be *applied* (base bundle unavailable at relaunch) fails loudly and instructs recovery via the full installer.

## Key API

- `BundleBuilder.DeltaFrom(string oldBundlePath)` -- enables delta mode
- `DeltaBundleCompiler.Compile(BundleModel model, string outputPath, string oldBundlePath)` -- generates delta bundles
- `DeltaCompressor.CreateDelta(Stream basisStream, Stream newStream, Stream outputStream)` -- low-level Octodiff delta creation (build side, `FalkForge.Compiler.Bundle`)
- `DeltaApplicator.ReconstructPayloadToFile(string deltaBundlePath, TocEntry deltaEntry, string basisBundlePath, string destinationDirectory, string relativeDestination)` -- install-time reconstruction of a delta payload against the base bundle (`FalkForge.Engine.Protocol.Bundle`), returning the resolved output path

## Notes

- Delta bundles use the same format as full bundles; the TOC marks entries as delta, and delta entries carry `BaseSha256Hash` (base payload) and `ReconstructedSha256Hash` (finished payload)
- Only payloads that actually changed get delta-compressed
- If a delta is larger than the full payload, the full payload is embedded instead
- The base bundle must remain available at update time; a delta cannot be applied without its exact base version
- Backward compatible: old engines ignore delta entries and download the full bundle
