# Demo 13: GlassForge — Glassmorphism Showcase

## Goal

A visually striking demo showing FalkForge's custom UI flexibility: a pill-shaped, semi-transparent dark glass window with a glowing cyan Install button — proving installers don't have to look boring.

## Visual Design

- **Shape**: 500x350px pill/stadium (CornerRadius=175, half height)
- **Background**: #1A1A2E at 85% opacity (frosted dark glass, desktop bleeds through)
- **Accent**: #00D4FF (cyan glow)
- **Layout** (vertically centered):
  - Top: "GlassForge" title — white, 24pt, light weight
  - Thin cyan separator line at 30% opacity
  - Center: Large INSTALL button (200x50, rounded 25px) with cyan gradient border, hover glow effect
  - Bottom: "EXIT" text button — white at 60% opacity, brightens on hover
- **Drag**: Entire window draggable via MouseLeftButtonDown → DragMove()

## Architecture

- Custom `GlassWindow` (Window subclass) with `AllowsTransparency=True`, `WindowStyle=None`, `Background=Transparent`
- Single `InstallerPage<InstallView>` — no navigation bar, no multi-page flow
- Entry point: `InstallerApp.Run()` with `.CustomWindow<GlassWindow>()`
- Install button triggers `Engine.PlanAsync` + `Engine.ApplyAsync`
- Exit button calls `Application.Current.Shutdown()`

## Files

```
demo/13-glass-ui/
  13-glass-ui.csproj
  Program.cs
  GlassWindow.xaml + .cs
  Pages/InstallPage.cs
  Views/InstallView.xaml
```
