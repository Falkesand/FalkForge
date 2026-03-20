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

The bundle engine:
1. Checks the update feed
2. Downloads the delta bundle (small)
3. Extracts cached v1 payloads from the local package cache
4. Applies Octodiff binary diffs to reconstruct v2 payloads
5. Verifies SHA-256 of each reconstructed payload
6. Falls back to full bundle download if any delta fails

## Key API

- `BundleBuilder.DeltaFrom(string oldBundlePath)` -- enables delta mode
- `DeltaBundleCompiler.Compile(model, outputPath, oldBundlePath)` -- generates delta bundles
- `DeltaCompressor.CreateDelta(byte[] basis, byte[] newData)` -- low-level Octodiff wrapper
- `DeltaApplicator.Apply(byte[] basis, byte[] delta, string expectedSha256)` -- engine-side reconstruction

## Notes

- Delta bundles use the same format as full bundles; the TOC marks entries as delta
- Only payloads that actually changed get delta-compressed
- If a delta is larger than the full payload, the full payload is embedded instead
- Backward compatible: old engines ignore delta entries and download the full bundle
