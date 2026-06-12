# Demo 42: Update Feed

Configures the bundle to check a remote JSON feed for available updates and, with a download
policy, to download a newer bundle, verify it, and launch it on the user's confirmation.

## What This Demonstrates

- Configuring an update feed URL for automatic version checking
- Selecting an update policy that downloads and prompts (`DownloadAndPrompt`)
- Pinning the update publisher's Authenticode thumbprint so only a correctly-signed update launches
- The end-to-end update loop: check → download → verify → prompt → launch → handoff

## Key API Calls

| Method                                | Purpose                                                             |
|---------------------------------------|--------------------------------------------------------------------|
| `.UpdateFeed(url, policy)`            | Set the update feed URL and update behavior policy                 |
| `UpdatePolicy.DownloadAndPrompt`      | Download the update in the background, then prompt the user        |
| `.PinUpdatePublisher(thumbprint)`     | Pin the certificate thumbprint the update bundle must be signed by |

## Update Policies

| Policy              | Behavior                                                                                   |
|---------------------|--------------------------------------------------------------------------------------------|
| `NotifyOnly`        | Check the feed and notify the UI that an update exists; no download, no launch             |
| `DownloadAndPrompt` | Check, background-download (delta-first, SHA-256 verified), then launch only on user action |
| `AutoUpdate`        | Check, download, then launch automatically (or prompt first if `PromptBeforeAutoUpdate`)   |

## How the Loop Works

1. At startup the engine fetches the feed JSON and compares versions.
2. For `DownloadAndPrompt` / `AutoUpdate` it downloads the newer bundle in the background,
   trying a delta download first and falling back to the full bundle, verifying the SHA-256.
3. The engine reports download progress and an "update ready" signal to the UI.
4. When the user chooses to install (or automatically, for `AutoUpdate` without a prompt), the
   engine verifies the bundle's Authenticode signature against the pinned thumbprint, launches
   the downloaded bundle, and shuts itself down so the two installers do not run at once.

## How to Build

```bash
dotnet build demo/42-bundle-update-feed/42-bundle-update-feed.csproj
```

## Notes

- The feed URL must use HTTPS and return a JSON document the engine can parse to determine
  whether a newer version is available, including the download URL and SHA-256 hash.
- `PinUpdatePublisher` expects a 40-character hexadecimal SHA-1 thumbprint. An invalid thumbprint
  is rejected at compile time with validation error `BDL031`. When set, a downloaded update whose
  certificate thumbprint does not match exactly is refused before it is launched.
- `UpdateFeed` and `UpdatePolicy` come from the `FalkForge.Engine.Protocol.Manifest` namespace.
