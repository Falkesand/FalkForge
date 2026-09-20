# Demo 39: Remote Payload

Shows the authoring API for an MSI package whose bytes are meant to be downloaded instead of embedded.

> **Current limitation:** `RemotePayload()` metadata is emitted into the manifest, but the ordinary chain install path does not yet download or cache it. This demo validates authoring and compilation only. Do not ship a bundle that depends on this path until runtime wiring lands.

## What This Demonstrates

- Declaring a remote payload with a download URL, SHA-256 hash, and file size
- Keeping the bundle lightweight by not embedding the MSI
- The metadata needed for future hash-verified download and progress reporting

## Key API Calls

| Method                              | Purpose                                                        |
|-------------------------------------|----------------------------------------------------------------|
| `.RemotePayload(url, sha256, size)` | Record the remote package URL, hash, and size in the manifest |
| First parameter: `string url`       | The HTTPS URL where the MSI is hosted                          |
| Second parameter: `string sha256`   | SHA-256 hash of the file for integrity verification            |
| Third parameter: `long size`        | Expected file size in bytes (10485760 = 10 MB)                 |

## How to Build

```bash
dotnet build demo/39-bundle-remote-payload/39-bundle-remote-payload.csproj
```

## Notes

- The compiler does not require the MSI to exist locally when `RemotePayload()` is used.
- The SHA-256 and size are preserved in the manifest, but the production chain runner does not consume them yet.
- For a supported downloadable path today, use an external container declared with `Container(id, c => c.DownloadUrl(...))`. The compiler emits the container beside the bundle and the engine downloads, verifies, and extracts it.
