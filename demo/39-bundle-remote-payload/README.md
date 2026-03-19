# Demo 39: Remote Payload

Configures an MSI package to be downloaded from a remote URL at install time instead of being embedded in the bundle.
This significantly reduces the bundle's file size when packages are large or hosted on a CDN.

## What This Demonstrates

- Declaring a remote payload with a download URL, SHA-256 hash, and file size
- Keeping the bundle lightweight by not embedding the MSI
- Integrity verification of downloaded payloads via hash

## Key API Calls

| Method                              | Purpose                                                        |
|-------------------------------------|----------------------------------------------------------------|
| `.RemotePayload(url, sha256, size)` | Specify that this package should be downloaded at install time |
| First parameter: `string url`       | The HTTPS URL where the MSI is hosted                          |
| Second parameter: `string sha256`   | SHA-256 hash of the file for integrity verification            |
| Third parameter: `long size`        | Expected file size in bytes (10485760 = 10 MB)                 |

## How to Build

```bash
dotnet build demo/39-bundle-remote-payload/39-bundle-remote-payload.csproj
```

## Notes

- The SHA-256 hash must match the remote file exactly; a mismatch causes the install to fail.
- The file size is used to display accurate download progress in the bootstrapper UI.
- The URL should use HTTPS to protect against man-in-the-middle attacks during download.
- The MSI file does not need to exist locally at compile time when using a remote payload.
