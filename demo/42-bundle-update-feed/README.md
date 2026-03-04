# Demo 42: Update Feed

Configures the bundle to check a remote JSON feed for available updates. The bootstrapper can notify the user about
newer versions or automatically trigger an update, depending on the chosen policy.

## What This Demonstrates

- Configuring an update feed URL for automatic version checking
- Selecting an update policy (notify-only vs. automatic)
- Integrating update awareness into the bootstrapper lifecycle

## Key API Calls

| Method                     | Purpose                                             |
|----------------------------|-----------------------------------------------------|
| `.UpdateFeed(url, policy)` | Set the update feed URL and update behavior policy  |
| `UpdatePolicy.NotifyOnly`  | Show an update notification but do not auto-install |

## How to Build

```bash
dotnet build demo/42-bundle-update-feed/42-bundle-update-feed.csproj
```

## Notes

- The feed URL should return a JSON document that the bootstrapper engine can parse to determine whether a newer version
  is available.
- `UpdatePolicy.NotifyOnly` informs the user without forcing an update. Other policies may allow silent or automatic
  updates.
- The `UpdateFeed` method requires the `FalkForge.Engine.Protocol.Manifest` namespace for the `UpdatePolicy` enum.
