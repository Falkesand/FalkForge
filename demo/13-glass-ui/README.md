# Demo 13: Glass UI

A fully custom installer window with a translucent, rounded-corner "glass" aesthetic. This demo shows how to replace the
default FalkForge window shell with a custom WPF `Window` subclass while still using the page navigation framework.

## What This Demonstrates

- Replacing the default installer window with a custom WPF Window via `CustomWindow<T>()`
- Transparent, borderless window with `AllowsTransparency="True"` and `Background="Transparent"`
- Rounded pill-shaped border using `CornerRadius="175"` with gradient border brush
- Semi-transparent dark background (`Opacity="0.85"`) for a frosted glass effect
- Custom drag-to-move via `MouseLeftButtonDown` and `DragMove()`
- Programmatic install flow using `Engine.PlanAsync` and `Engine.ApplyAsync` directly from page code
- Single-page installer layout (no wizard steps)

## Key API Calls

```csharp
InstallerApp.Run(args, app => app
    .Window(w => w
        .CustomWindow<GlassWindow>()   // Use custom WPF Window subclass
        .Size(500, 350)
        .Borderless()
        .Title("GlassForge"))
    .Pages(p => p
        .Add<InstallPage>()));

// Direct engine control from a page
public async Task InstallAsync()
{
    await Engine.PlanAsync(InstallAction.Install);
    await Engine.ApplyAsync();
}
```

```xml
<!-- GlassWindow.xaml: transparent window with rounded border -->
<Window WindowStyle="None" AllowsTransparency="True" Background="Transparent">
    <Border CornerRadius="175" ClipToBounds="True" BorderThickness="1">
        <Border.Background>
            <SolidColorBrush Color="#1A1A2E" Opacity="0.85"/>
        </Border.Background>
        <ContentPresenter Content="{Binding CurrentView}"/>
    </Border>
</Window>
```

## How to Build

```
dotnet build demo/13-glass-ui/13-glass-ui.csproj
```

## Notes

- `CustomWindow<T>()` tells the framework to instantiate your WPF Window class instead of the default shell. The window
  must contain a `ContentPresenter` bound to `{Binding CurrentView}` so pages render inside it.
- The `GlassWindow` code-behind wires `DragMove()` on `MouseLeftButtonDown`, but excludes `Button` and `TextBlock`
  elements to avoid interfering with click interactions.
- This demo has a single `InstallPage` with no Next/Back buttons -- the install is triggered programmatically, making it
  suitable for minimal or splash-screen style installers.
- `Engine.PlanAsync` and `Engine.ApplyAsync` are the low-level engine methods. Most demos use `PageResult.Install` which
  calls these internally, but direct access is available when you need custom orchestration.
