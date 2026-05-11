# RFC: Deepen MSI dialog templates into a layout DSL with customization facade

**Status:** COMPLETED 2026-05-11 — see commits f1f370f, dcabf3b, c7a4957, 530f25f and follow-on layout-DSL commits. See postscript at the bottom of this document.
**Author:** architectural review, 2026-04-11
**Scope:** `src/FalkForge.Compiler.Msi/UI/`, `src/FalkForge.Core/Models/UI/`, `src/FalkForge.Core/Builders/PackageBuilder.cs`, `src/FalkForge.Extensibility/`, `tests/FalkForge.Compiler.Msi.Tests/UI/`

## Problem

`src/FalkForge.Compiler.Msi/UI/Templates/SharedDialogBuilders.cs` is a 798-LOC static class with 7 builder methods (`BuildWelcomeDlg`, `BuildLicenseAgreementDlg`, `BuildInstallDirDlg`, `BuildCustomizeDlg`, `BuildProgressDlg`, `BuildExitDlg`, `BuildSetupTypeDlg`). Each method hand-places dialog control coordinates as literal integers: title text at `X=15 Y=6`, description at `X=25 Y=23`, button row at `Y=243` with buttons at `X=180 / 236 / 304`, width 56, height 17. Every builder method repeats these coordinates for its button row and banner area. The same literals appear 16 or more times across `SharedDialogBuilders` and `MinimalDialogTemplate`, which also contains an inline `BuildWelcomeDlg` variant.

Five templates (`MinimalDialogTemplate`, `InstallDirDialogTemplate`, `FeatureTreeDialogTemplate`, `MondoDialogTemplate`, `AdvancedDialogTemplate`) consume `SharedDialogBuilders` by calling the builder methods and assembling the resulting `MsiDialogModel` instances into a `IReadOnlyList`. The downstream `DialogEmitter` (330 LOC) consumes this list, resolves `!(loc.X)` localization tokens via the existing `LocalizedStringResolver`, and emits SQL INSERT rows into the MSI database's `Dialog`, `Control`, `ControlEvent`, and `ControlCondition` tables. The emitter side and the model layer (`MsiDialogModel`, `MsiControlModel`, `MsiControlEventModel`, `MsiControlConditionModel`) are clean — they use immutable `required init` properties and work in dialog units (DLU), the MSI-standard scaled coordinate system where 1 DLU is approximately one quarter of the dialog font's average character width.

**The "wider button" pain is the canonical symptom.** If a UI designer wants to change the standard button width from 56 DLU to 70 DLU, the edit requires touching roughly 16 places across `SharedDialogBuilders.cs` and `MinimalDialogTemplate.cs`. Every `BuildXxxDlg` method rebuilds its own button row with hand-placed coordinates; there is no `BuildButtonRow()` helper, no shared constant, no layout primitive. If the edit misses one site, that template silently emits a different button width than the others, and the diagnostic path is "read every call site manually and compare integers."

The root cause is that buttons are laid out by absolute coordinates, not by membership in a named region with auto-positioning policy. There is no `ButtonRow` type, no `BannerArea` type, no `ContentArea` type. The concept of "the dialog has a title row at the top, a content area in the middle, a bottom line separator, and a button row at the bottom" exists only in the programmer's head. Nothing in code represents the layout of a dialog as a composition of named regions.

A second consequence is that there is no public customization API. `IDialogTemplate` is `internal`. `DialogEmitter.GetTemplate()` is a `switch` over the `MsiDialogSet` enum. A package author who wants "FeatureTree but with my company logo in the banner" has two bad options: implement `IDialogTemplate` from scratch by copying `SharedDialogBuilders` into a new class and editing the banner code path, or fork the FalkForge source. There is no way to say "take the FeatureTree template and swap one bitmap" declaratively.

A third consequence is that per-dialog unit testing is impractical. The builder methods are static and coupled to `SharedDialogBuilders`'s internal coordinate literals. Testing `BuildWelcomeDlg` in isolation means asserting on `MsiControlModel.X` and `MsiControlModel.Y` values that are themselves hardcoded in the same file. The existing 30 tests in `DialogEmitterTests.cs` and `DialogSetTests.cs` cover the full compile pipeline — they run `MsiCompiler.Compile`, write a real `.msi` file, query the resulting tables, and assert on row existence. These are good integration tests but they do not catch coordinate drift or button positioning regressions. Zero unit tests exist for the builder layer.

A fourth consequence is that extensions have no way to contribute custom dialog steps. An extension (e.g., a hypothetical Licensing extension that wants a "license key entry" dialog) cannot register a dialog step that appears between Welcome and Customize. The only path is to fork the template set.

**Non-problems** that this refactor deliberately does not touch:

- **Localization**: `!(loc.Dialog.Welcome.Title)` tokens are resolved correctly by `DialogEmitter.ResolveDialogStrings` via `LocalizedStringResolver` with culture fallback. The resolution pass runs after the builders return their `MsiDialogModel` instances and before SQL emission. No fix needed.
- **MSI model layer**: `MsiDialogModel`, `MsiControlModel`, `MsiControlEventModel`, `MsiControlConditionModel` are clean immutable records. No fix needed.
- **`DialogEmitter`**: the 330-LOC downstream orchestrator handles table creation, row emission, install sequence wiring, and localization pass correctly. No fix needed.
- **`IDialogTemplate` contract**: the `GetDialogs(package)` shape is fine. Only the internals need work.

This is a shallow-module problem concentrated in `SharedDialogBuilders.cs` — 798 LOC of repeated coordinate literals with no layout primitives, no customization API, no per-dialog unit testability, and no extension contribution mechanism. Deepening means introducing a grid-based layout DSL with named regions (internal), a fluent customization builder exposed through a new `UseDialogSet` overload (public 5% caller surface), and a separate `RegisterDialogStep` path on `ExtensionContext` for extensions that want to contribute behavior-carrying dialog steps.

## Proposed Interface

The design splits into three layers: a preserved 95% caller entry point, a new public customization API for the 5% caller, and an internal grid layout DSL that replaces the hand-placed coordinates inside the builders.

### Public facade — 95% caller unchanged

```csharp
namespace FalkForge.Core.Builders;

public sealed class PackageBuilder
{
    // Existing — 95% caller, zero churn
    public PackageBuilder UseDialogSet(MsiDialogSet dialogSet);

    // New — 5% caller customization entry
    public PackageBuilder UseDialogSet(
        MsiDialogSet dialogSet,
        Action<DialogCustomization> configure);
}
```

Real 95% caller:

```csharp
var package = PackageBuilder.Create("MyApp", "1.0.0", "Acme Corp")
    .UseDialogSet(MsiDialogSet.FeatureTree)
    .Build();
```

Zero imports from `FalkForge.Models.UI`, zero customization surface.

### Public customization — 5% caller

```csharp
namespace FalkForge.Models.UI;

/// <summary>
/// Fluent builder for declarative dialog customization. Methods express
/// high-level intent (swap a banner, relabel a button, suppress a dialog)
/// without exposing MsiControlModel or any DLU coordinate. The builder
/// produces a frozen DialogCustomizationModel stored on PackageModel and
/// consumed by DialogEmitter at compile time.
/// </summary>
public sealed class DialogCustomization
{
    /// <summary>
    /// Reference to a binary table entry (added via PackageBuilder.AddBinary)
    /// used as the banner bitmap in interior dialogs. Null means template default.
    /// </summary>
    public DialogCustomization BannerBitmap(string binaryKey);

    /// <summary>
    /// Reference to a binary table entry used as the watermark in exterior
    /// (welcome and exit) dialogs. Null means template default.
    /// </summary>
    public DialogCustomization DialogBitmap(string binaryKey);

    /// <summary>
    /// Reference to a binary table entry used as the header icon.
    /// </summary>
    public DialogCustomization HeaderIcon(string binaryKey);

    /// <summary>
    /// Override the label of a stock button across every dialog in the set.
    /// Accepts a literal string ("Continue") or a localization key
    /// ("!(loc.Button.MyContinue)"). Label resolution happens in DialogEmitter
    /// via the existing LocalizedStringResolver.
    /// </summary>
    public DialogCustomization OverrideButtonLabel(DialogButton button, string textOrLocKey);

    /// <summary>
    /// Override the window title shown in the dialog's title bar.
    /// Defaults to "[ProductName] Setup".
    /// </summary>
    public DialogCustomization WindowTitle(string titleOrLocKey);

    /// <summary>
    /// Suppress a stock dialog from the flow. Validator DLG002 rejects
    /// suppressions that would break navigation wiring (e.g., suppressing
    /// License when Welcome's Next button chains to it).
    /// </summary>
    public DialogCustomization SuppressDialog(StockDialog dialog);

    /// <summary>
    /// Insert a named extension-contributed dialog step after the specified
    /// stock dialog. The step must have been registered via
    /// ExtensionContext.RegisterDialogStep by an IFalkForgeExtension.
    /// Validator DLG001 rejects unknown step names.
    /// </summary>
    public DialogCustomization InsertStep(string stepName, StockDialog after);
}

public enum DialogButton { Next, Back, Cancel, Install, Finish, Browse, Print, Remove, Repair }

public enum StockDialog
{
    Welcome, License, InstallDir, Features, Ready, Progress, Exit, Maintenance,
    Extension  // marker for extension-contributed dialogs
}
```

Real 5% caller:

```csharp
var package = PackageBuilder.Create("MyApp", "1.0.0", "Acme Corp")
    .AddBinary("AcmeBanner", "assets/banner.bmp")
    .AddBinary("AcmeWatermark", "assets/watermark.bmp")
    .UseDialogSet(MsiDialogSet.FeatureTree, dialogs => dialogs
        .BannerBitmap("AcmeBanner")
        .DialogBitmap("AcmeWatermark")
        .OverrideButtonLabel(DialogButton.Install, "!(loc.Button.Deploy)")
        .OverrideButtonLabel(DialogButton.Next, "Continue")
        .WindowTitle("Acme Studio [ProductName]"))
    .Build();
```

### Public extension contract — behavior-carrying dialog contributions

```csharp
namespace FalkForge.Extensibility;

/// <summary>
/// An extension-contributed dialog step. Lives in an extension assembly,
/// registered via ExtensionContext.RegisterDialogStep during the extension's
/// Register() callback. Identified by Name; positioned via DialogCustomization.InsertStep.
/// </summary>
public interface IDialogStepBuilder
{
    /// <summary>Stable identifier referenced by DialogCustomization.InsertStep(name, after:).</summary>
    string Name { get; }

    /// <summary>Kind classification. Extensions return StockDialog.Extension.</summary>
    StockDialog Kind { get; }

    /// <summary>Builds the dialog model. Takes a build context providing
    /// the package model, customization state, and registered step registry.</summary>
    MsiDialogModel Build(DialogBuildContext context);
}

public interface IExtensionRegistry
{
    // ... existing methods ...
    void RegisterDialogStep(IDialogStepBuilder builder);
}
```

Extension author:

```csharp
public sealed class LicensingExtension : IFalkForgeExtension
{
    public string Name => "Licensing";

    public void Register(ExtensionContext context)
    {
        context.RegisterDialogStep(new LicenseKeyDlgBuilder());
    }
}

internal sealed class LicenseKeyDlgBuilder : IDialogStepBuilder
{
    public string Name => "LicenseKeyDlg";
    public StockDialog Kind => StockDialog.Extension;

    public MsiDialogModel Build(DialogBuildContext context)
    {
        // Construct the dialog using internal layout primitives if reusing
        // the standard canvas, or by hand if a fully custom layout is needed.
        // ...
    }
}
```

Package author uses the extension:

```csharp
var package = PackageBuilder.Create("MyApp", "1.0.0", "Acme Corp")
    .UseExtension(new LicensingExtension())
    .UseDialogSet(MsiDialogSet.FeatureTree, dialogs => dialogs
        .InsertStep("LicenseKeyDlg", after: StockDialog.License))
    .Build();
```

The split between `DialogCustomization` (inline lambda, data-only) and `IDialogStepBuilder` (class in an extension assembly, behavior-carrying) is deliberate: an inline lambda cannot ship with a reusable extension assembly, and making it do so would reinvent `IFalkForgeExtension`.

### Internal grid layout DSL

```csharp
namespace FalkForge.Compiler.Msi.UI.Layout;

/// <summary>
/// Rectangular region in dialog units (DLU).
/// </summary>
internal readonly record struct Rect(int X, int Y, int W, int H);

/// <summary>
/// Named region within a dialog canvas. Controls are placed inside a region
/// and auto-positioned according to the region's policy. Changing a region's
/// bounds or policy updates every dialog using that layout.
/// </summary>
internal sealed record DialogRegion(
    string Name,
    Rect Bounds,
    RegionPolicy Policy,
    RegionDefaults Defaults);

internal enum RegionPolicy
{
    /// <summary>Children use their explicit X/Y within the region. Used for title row and content area.</summary>
    Absolute,

    /// <summary>Children are packed right-to-left from the region's right edge with fixed gaps. Used for button row.</summary>
    RightPacked,

    /// <summary>Children are stacked top-to-bottom from the region's top edge with fixed gaps.</summary>
    TopStacked,

    /// <summary>Region contains exactly one child centered in the region. Used for banner bitmap.</summary>
    SingleControl
}

internal sealed record RegionDefaults(
    int ChildWidth = 56,
    int ChildHeight = 17,
    int Gap = 8,

    /// <summary>
    /// Per-child gap override array used only during cutover. The legacy
    /// SharedDialogBuilders code places buttons with non-uniform gaps:
    /// Cancel-to-Next is 12 DLU, Next-to-Back is 0 DLU. This array
    /// reproduces the legacy gap sequence exactly so the byte-identical
    /// cutover test passes. Post-cutover follow-up commit collapses to
    /// uniform Gap = 8.
    /// </summary>
    ImmutableArray<int> Gaps = default);

/// <summary>
/// Immutable dialog layout definition. Named regions stored in an
/// ImmutableArray with a FrozenDictionary index for fast lookup.
/// Overrides produce new layouts via With().
/// </summary>
internal sealed class DialogLayout
{
    public string Name { get; }
    public int CanvasWidth { get; }
    public int CanvasHeight { get; }
    public ImmutableArray<DialogRegion> Regions { get; }
    public FrozenDictionary<string, int> RegionIndex { get; }

    public DialogLayout(string name, int canvasWidth, int canvasHeight, ImmutableArray<DialogRegion> regions);

    public DialogRegion? TryGet(string regionName);
    public DialogLayout With(string regionName, DialogRegion replacement);
}

/// <summary>
/// Standard built-in dialog layouts. The 5 stock templates all use
/// Standard370x270. Future wizard-style variants add their own entries here.
/// </summary>
internal static class Layouts
{
    /// <summary>
    /// Canonical FalkForge dialog layout. 370 DLU wide, 270 DLU tall.
    /// Regions: Banner (0,0,370,58), TitleRow (15,6,200,15),
    /// ContentArea (15,60,340,165), BottomLine (0,234,370,0),
    /// ButtonRow (0,243,360,17) with RightPacked policy.
    /// </summary>
    public static DialogLayout Standard370x270 { get; }
}
```

### Internal dialog composition

```csharp
namespace FalkForge.Compiler.Msi.UI.Layout;

/// <summary>
/// Declarative description of what a dialog contains. Templates produce
/// DialogContent instances; DialogComposer combines content with a layout
/// and customization to produce the MsiDialogModel consumed by DialogEmitter.
/// </summary>
internal sealed record DialogContent(
    string DialogName,
    StockDialog Kind,
    ImmutableArray<RegionPlacement> Placements,
    ImmutableArray<MsiControlEventModel> Events,
    ImmutableArray<MsiControlConditionModel> Conditions,
    string FirstControl,
    string DefaultControl,
    string CancelControl,
    string TitleLocKey);

internal sealed record RegionPlacement(
    string RegionName,
    ImmutableArray<PlacedControl> Controls);

internal sealed record PlacedControl(
    string Name,
    MsiControlType Type,
    string? TextOrLocKey,
    string? Property,
    int? WidthOverride = null,
    int? HeightOverride = null,
    MsiControlAttributes Attributes = default);

/// <summary>
/// Pure static composer that merges a DialogContent, a DialogLayout, and
/// a DialogCustomizationModel into a final MsiDialogModel. The composer
/// applies region policies (RightPacked auto-positions buttons, Absolute
/// respects explicit placement), resolves customization overrides
/// (banner bitmap swap, button label rewrite), and produces the
/// coordinate-complete model for DialogEmitter.
/// </summary>
internal static class DialogComposer
{
    public static MsiDialogModel Compose(
        DialogContent content,
        DialogLayout layout,
        DialogCustomizationModel customization);
}
```

### Internal per-dialog builders

Seven small classes under `Templates/Builders/`, each approximately 50-100 LOC. Each implements `IDialogStepBuilder` (so they are interchangeable with extension-contributed builders). Each produces a `DialogContent` and hands it to `DialogComposer.Compose` with the standard layout.

```csharp
internal sealed class WelcomeDlgBuilder : IDialogStepBuilder
{
    public string Name => "WelcomeDlg";
    public StockDialog Kind => StockDialog.Welcome;

    public MsiDialogModel Build(DialogBuildContext context)
    {
        var content = new DialogContent(
            DialogName: "WelcomeDlg",
            Kind: StockDialog.Welcome,
            Placements: ImmutableArray.Create(
                new RegionPlacement("Banner", ImmutableArray.Create(
                    new PlacedControl("BannerBitmap", MsiControlType.Bitmap, TextOrLocKey: "BannerBmp", Property: null))),
                new RegionPlacement("TitleRow", ImmutableArray.Create(
                    new PlacedControl("Title", MsiControlType.Text, TextOrLocKey: "!(loc.Dialog.Welcome.Title)", Property: null))),
                new RegionPlacement("ContentArea", ImmutableArray.Create(
                    new PlacedControl("Description", MsiControlType.Text, TextOrLocKey: "!(loc.Dialog.Welcome.Description)", Property: null))),
                new RegionPlacement("ButtonRow", ImmutableArray.Create(
                    new PlacedControl("Cancel", MsiControlType.PushButton, TextOrLocKey: "!(loc.Button.Cancel)", Property: null),
                    new PlacedControl("Next", MsiControlType.PushButton, TextOrLocKey: "!(loc.Button.Next)", Property: null)))),
            Events: ImmutableArray.Create(
                new MsiControlEventModel { DialogName = "WelcomeDlg", ControlName = "Next", Event = MsiControlEvent.NewDialog, Argument = DialogNames.LicenseAgreement },
                new MsiControlEventModel { DialogName = "WelcomeDlg", ControlName = "Cancel", Event = MsiControlEvent.SpawnDialog, Argument = DialogNames.Cancel }),
            Conditions: ImmutableArray<MsiControlConditionModel>.Empty,
            FirstControl: "Next",
            DefaultControl: "Next",
            CancelControl: "Cancel",
            TitleLocKey: "!(loc.Dialog.Welcome.WindowTitle)");

        return DialogComposer.Compose(content, Layouts.Standard370x270, context.Customization);
    }
}
```

Notable properties:

- No DLU coordinate literal appears in the builder. The welcome dialog says "put the title in TitleRow, the description in ContentArea, Cancel and Next in ButtonRow" — the layout computes coordinates from the region policies.
- `ButtonRow` is `RegionPolicy.RightPacked`. The composer places `Cancel` at the right edge of the region and `Next` to the left of it with the specified gap. No builder touches the `X=236 / 304` literals.
- Customization is applied by `DialogComposer.Compose`. If `customization.BannerBitmap` is set, the "BannerBmp" text on the banner control is replaced with the custom binary key. If `customization.OverrideButtonLabel(DialogButton.Next, "Continue")` is set, the "Next" control's text becomes "Continue".

### What the deepened module owns

- `DialogLayout` as the canonical layout type with named regions and override semantics. The Standard370x270 layout is the single source of truth for DLU coordinates.
- `DialogRegion` with `RegionPolicy` auto-positioning (Absolute for title/content, RightPacked for button row, SingleControl for banner bitmap, TopStacked for future use).
- `DialogContent` as the declarative dialog shape — placements, events, conditions, tab chain — independent of coordinates.
- `DialogComposer.Compose` as the pure function that combines content + layout + customization into the final `MsiDialogModel`.
- Seven per-dialog builders implementing `IDialogStepBuilder`, each approximately 50-100 LOC, each individually unit-testable.
- `DialogCustomization` public fluent builder for declarative overrides (banner bitmap, button labels, window title, dialog suppression, extension step insertion).
- `DialogCustomizationModel` frozen immutable storage of customization state, referenced by `PackageModel.DialogCustomization`.
- `IDialogStepBuilder` public contract for extension-contributed dialog steps, registered via `ExtensionContext.RegisterDialogStep`.
- Byte-identical cutover test suite comparing new DSL output against legacy `SharedDialogBuilders` output per template per control.

### What the deepened module hides

- The 798 LOC of `SharedDialogBuilders.cs`. Deleted after cutover.
- Hardcoded DLU coordinate literals scattered across builder methods.
- The 16-site duplication of button row positioning.
- `MinimalDialogTemplate`'s inline `BuildWelcomeDlg` variant. Folded into the unified `WelcomeDlgBuilder`.
- `MsiControlModel`, `MsiControlType`, `MsiControlAttributes` from the public 5% customization surface. Package authors see `DialogButton` and `StockDialog` enums, never individual control types.
- The pixel-packing arithmetic for `RegionPolicy.RightPacked`. Done once in `DialogComposer`, invisible to builders.

## Dependency Strategy

This module is pure in-process layout computation. No I/O, no async, no external dependencies beyond `System.Collections.Immutable` and `System.Collections.Frozen`. The data flow:

```
PackageBuilder
  .UseDialogSet(set)                        -> _dialogSet = set
  .UseDialogSet(set, cfg)                   -> run cfg over new DialogCustomization() -> ToModel()
      |
      v
PackageModel { DialogSet, DialogCustomization }  (init-only fields in Core)
      |
      v
MsiCompiler -> MsiAuthoring -> MsiRecipeBuilder -> DialogSetProducer
      |
      v
DialogBuildContext { Package, Customization, StepRegistry }
      |
      v
IDialogTemplate.GetDialogs(context)
      |
      v
(per dialog in template sequence)
IDialogStepBuilder.Build(context)
      |  produces DialogContent
      v
DialogComposer.Compose(content, Layouts.Standard370x270, customization)
      |  produces MsiDialogModel with final coordinates
      v
IReadOnlyList<MsiDialogModel>
      |
      v
DialogEmitter.ResolveDialogStrings(...)     (existing localization pass, unchanged)
      |
      v
DialogEmitter.EmitDialog(...)               (existing SQL emission, unchanged)
```

### Core has no dependency on Compiler.Msi

`DialogCustomization` and `DialogCustomizationModel` live in `FalkForge.Core/Models/UI/`. `DialogButton` and `StockDialog` enums live there too. `PackageModel.DialogCustomization` is an init-only nullable property in Core. The grid layout DSL (`DialogLayout`, `DialogRegion`, `DialogComposer`, `IDialogStepBuilder`, `DialogBuildContext`) lives in `FalkForge.Compiler.Msi.UI.Layout` because it directly consumes `MsiDialogModel` and `MsiControlModel`. The dependency direction stays Core → Compiler.Msi.

### Extension registration

`ExtensionContext.RegisterDialogStep(IDialogStepBuilder)` adds a new method on the existing plugin registry. The extension host enumerates `IFalkForgeExtension` implementations at `Installer.Build()` time and calls each extension's `Register` method with the `ExtensionContext`. Steps registered during this phase are collected into a `FrozenDictionary<string, IDialogStepBuilder>` stored on the `DialogBuildContext` passed to each template. Templates check the customization's `InsertStep` calls and look up the step by name at composition time.

No IoC container. No reflection. No attribute scanning. NativeAOT-safe.

### Per-builder unit testing

Each `IDialogStepBuilder` is a plain class with one public method (`Build`) taking a `DialogBuildContext`. Tests construct a minimal `DialogBuildContext.ForTest(...)` helper and call `Build` directly:

```csharp
[Fact]
public void WelcomeDlgBuilder_DefaultCustomization_PlacesTitleInBannerRegion()
{
    var package = PackageModelStub.Minimal();
    var context = DialogBuildContext.ForTest(package, DialogCustomizationModel.Empty);

    var dialog = new WelcomeDlgBuilder().Build(context);

    var title = dialog.Controls.Single(c => c.Name == "Title");
    Assert.Equal(15, title.X);
    Assert.Equal(6, title.Y);
    Assert.Equal("!(loc.Dialog.Welcome.Title)", title.Text);
}

[Fact]
public void WelcomeDlgBuilder_CustomBanner_ReplacesBannerBitmapKey()
{
    var package = PackageModelStub.Minimal();
    var customization = new DialogCustomization()
        .BannerBitmap("AcmeBanner")
        .ToModel();
    var context = DialogBuildContext.ForTest(package, customization);

    var dialog = new WelcomeDlgBuilder().Build(context);

    var banner = dialog.Controls.Single(c => c.Type == MsiControlType.Bitmap);
    Assert.Equal("AcmeBanner", banner.Text);
}
```

No `MsiDatabase`, no msi.dll, no `.msi` file on disk. Tests run in milliseconds on any OS.

## Testing Strategy

**Replace, don't layer.** The existing 30 integration tests (`DialogEmitterTests.cs`, `DialogSetTests.cs`) stay as smoke tests through the cutover. Per-builder unit tests are added alongside them. After the byte-identical cutover test is green, the legacy `SharedDialogBuilders.cs` and its call sites are deleted.

### New boundary tests to write

At the per-builder level (one test class per builder, approximately 5 tests per class, approximately 7 × 5 = 35 tests):

1. **Default content produces expected controls** — for each builder, assert the generated `MsiDialogModel.Controls` contains the expected control names, types, and text tokens.
2. **Region coordinate regression** — assert each control's `X`, `Y`, `Width`, `Height` match the expected DLU values from `Layouts.Standard370x270`. Coordinate drift fails loudly.
3. **Tab chain ring** — assert `FirstControl`, `DefaultControl`, `CancelControl`, and each control's `NextControl` form a valid ring.
4. **Event wiring** — assert the expected `MsiControlEventModel` rows are emitted with the correct `DialogName`, `ControlName`, `Event`, and `Argument`.
5. **Customization overrides apply** — for each customization slot (banner, dialog bitmap, button label, window title), assert the override flows into the correct control.

At the `DialogComposer` level (approximately 10 tests):

6. **RightPacked policy auto-positions buttons** — construct a `DialogContent` with three buttons in the button row, assert coordinates match `ButtonRightEdge - N*Width - (N-1)*Gap`.
7. **Absolute policy respects explicit placement** — construct a content with absolute-placed controls, assert no repositioning.
8. **SingleControl policy centers the bitmap** — assert the banner bitmap is centered in the banner region.
9. **Layout `With` override** — construct a custom layout via `Layouts.Standard370x270.With("Banner", newBannerRegion)`, assert the composer uses the overridden region.
10. **Customization overrides banner key** — assert the banner control's text field is replaced with the custom binary key.
11. **Customization overrides button label across all dialogs** — apply `OverrideButtonLabel(Next, "Continue")` to a full dialog set, assert every `Next` button has the new label.
12. **SuppressDialog removes the dialog from the sequence** — assert the suppressed dialog is absent from the final template output.
13. **SuppressDialog that breaks navigation fails validation** — assert `DLG002` error when suppressing a dialog that other dialogs route to.
14. **InsertStep positions correctly** — register a stub `IDialogStepBuilder`, insert after License, assert the step appears in the sequence between License and the next dialog.
15. **InsertStep with unknown name fails validation** — assert `DLG001` error when the step name is not registered.

At the `Layouts.Standard370x270` level (approximately 5 tests):

16. **Canvas dimensions** — assert the canvas is 370 × 270 DLU.
17. **Region bounds** — assert each of the 5 canonical regions has the expected bounds.
18. **Region lookup** — `TryGet("Banner")` returns the banner region, `TryGet("Unknown")` returns null.
19. **Immutability** — `With("Banner", ...)` returns a new `DialogLayout` instance without mutating the original.
20. **Frozen dictionary index** — `RegionIndex` is a `FrozenDictionary` for O(1) lookup.

At the byte-identical cutover level (approximately 30 tests, retained until legacy deletion):

21-50. **Per-template byte-identical output** — for each of the 5 templates, compile a representative package with both the legacy `SharedDialogBuilders` path (kept alive during cutover) and the new DSL path, and assert that every emitted `MsiDialogModel` has identical control counts, identical control coordinates, identical event wiring, and identical text tokens. Cutover PR cannot merge until this is green for every template.

### Old tests to delete

- After cutover: `DialogEmitterTests.cs` and `DialogSetTests.cs` are reduced from 30 tests to approximately 10 that assert end-to-end .msi emission correctness (as smoke tests) — the per-builder unit tests replace the detailed coordinate assertions.
- The legacy `SharedDialogBuilders.cs` and `MinimalDialogTemplate.cs`'s inline `BuildWelcomeDlg` variant are deleted.
- The byte-identical cutover tests are deleted one release cycle after the cutover PR merges.

### Test environment needs

- `DialogBuildContext.ForTest(package, customization)` static helper.
- `PackageModelStub.Minimal()` returning a minimal valid `PackageModel` for tests.
- No new NuGet packages, no platform requirements. All tests run on Linux CI.

## Implementation Recommendations

Durable architectural guidance, decoupled from current file paths:

### What the module should own

- `DialogLayout` as the named-regions layout type with override semantics. Standard370x270 is the one place DLU coordinates live for the stock dialog set.
- `DialogRegion` with `RegionPolicy` auto-positioning. Button rows, title rows, content areas, and bitmap regions are all expressed as named regions with declarative policies.
- `DialogContent` as the declarative dialog shape independent of coordinates. Templates say "put a button in ButtonRow" not "put a button at X=236 Y=243".
- `DialogComposer.Compose` as the pure function combining content + layout + customization. One code path resolves all customization overrides.
- `DialogCustomization` fluent public builder for 5% callers who want to swap banners, relabel buttons, suppress dialogs, or insert extension steps.
- `IDialogStepBuilder` public contract for extension-contributed dialog steps.
- Per-builder unit testability via `DialogBuildContext.ForTest` and `PackageModelStub.Minimal`.
- Byte-identical cutover test suite gating the migration PR.

### What the module should hide

- All 798 LOC of `SharedDialogBuilders.cs`. Deleted.
- Hardcoded DLU coordinate literals.
- The 16-site duplication of button row positioning.
- `MsiControlModel` / `MsiControlType` / `MsiControlAttributes` from the public 5% customization surface.
- The layout DSL itself — internal, not part of the public API.

### What the module should expose

Two public surfaces:

1. **Facade** — `PackageBuilder.UseDialogSet(set)` for the 95% caller (unchanged), `PackageBuilder.UseDialogSet(set, Action<DialogCustomization>)` for the 5% caller. `DialogCustomization` fluent builder in `FalkForge.Models.UI`.
2. **Extension contract** — `IDialogStepBuilder` and `ExtensionContext.RegisterDialogStep` for extensions contributing behavior-carrying dialog steps.

### How callers should migrate

**95% caller** — no changes. `UseDialogSet(MsiDialogSet.FeatureTree)` works exactly as today.

**5% caller with banner / button customization** — adopts the new lambda overload:

```csharp
.UseDialogSet(MsiDialogSet.FeatureTree, dialogs => dialogs
    .BannerBitmap("MyBanner")
    .OverrideButtonLabel(DialogButton.Install, "Deploy"))
```

**Extension authors** — implement `IDialogStepBuilder` for their custom dialog steps, register via `context.RegisterDialogStep(new MyStepBuilder())` in their extension's `Register` method.

**Template authors (internal to FalkForge)** — existing templates are rewritten to use `DialogComposer.Compose` with declarative `DialogContent` and the standard layout. The 5 template classes shrink from approximately 60-150 LOC each to approximately 30-50 LOC each, simply composing `IDialogStepBuilder` instances in the right order.

### Implementation sequencing

TDD-driven, each phase gets its own implementation plan file. Sketch of order:

1. **Define core layout types** — `Rect`, `DialogRegion`, `RegionPolicy`, `RegionDefaults`, `DialogLayout`. Pure value types only, no behavior. Failing-first tests on construction, `With` override, `TryGet` lookup.
2. **Define `Layouts.Standard370x270`** — the 5 canonical regions with bounds matching the current hardcoded coordinates. Failing-first test asserting region bounds.
3. **Define `DialogContent` / `RegionPlacement` / `PlacedControl`** — declarative dialog shape records.
4. **Stand up `DialogComposer.Compose` skeleton** — takes a minimal `DialogContent` with no customization, produces an empty `MsiDialogModel`. Failing-first test.
5. **Implement `RegionPolicy.Absolute`** — controls with explicit placement in title/content regions. Failing-first test.
6. **Implement `RegionPolicy.RightPacked`** — button row auto-positioning. Failing-first test asserting 3-button coordinates.
7. **Implement `RegionPolicy.SingleControl`** — centered banner bitmap. Failing-first test.
8. **Port `WelcomeDlgBuilder`, TDD** — failing-first per-builder unit test, implement the builder, green. Then `LicenseDlgBuilder`, `InstallDirDlgBuilder`, `CustomizeDlgBuilder`, `ProgressDlgBuilder`, `ExitDlgBuilder`, `SetupTypeDlgBuilder`. One builder per commit.
9. **Implement `DialogCustomizationModel` and `DialogCustomization` builder** — failing-first tests on each verb (`BannerBitmap`, `OverrideButtonLabel`, etc.), implement the builder's `ToModel()` freeze.
10. **Wire `PackageBuilder.UseDialogSet(set, Action<DialogCustomization>)` overload** — failing-first integration test, implement the overload, assert `PackageModel.DialogCustomization` is populated.
11. **Apply customization in `DialogComposer.Compose`** — failing-first tests for each override slot, implement the override pass in the composer.
12. **Rewrite the 5 templates to use the new builders** — each template becomes approximately 30-50 LOC composing the right sequence of `IDialogStepBuilder` instances. One template per commit.
13. **Byte-identical cutover test** — for each of the 5 templates, compile a representative package with both legacy and new paths, assert byte-for-byte equality at the `MsiDialogModel` level. This is the gate.
14. **Flip `DialogEmitter.GetTemplate`** — switch from constructing legacy template classes to constructing the new builder-composed templates. Run the full test suite. Green means cutover is safe.
15. **Delete legacy** — remove `SharedDialogBuilders.cs`, the inline `BuildWelcomeDlg` in `MinimalDialogTemplate.cs`, and the old template implementations. One cleanup commit.
16. **Add `IDialogStepBuilder` public contract and `ExtensionContext.RegisterDialogStep`** — failing-first test with a stub extension step builder, implement the registration path, assert the step can be inserted via `DialogCustomization.InsertStep`.
17. **Implement `DLG001` and `DLG002` validators** — failing-first tests for unknown step insertion and broken-navigation suppression.
18. **Delete byte-identical cutover tests** — one follow-up commit one release cycle after cutover.
19. **Documentation** — update `docs/` with the layout DSL architecture, the customization guide, and the "contribute a dialog step" extension guide.

Each phase of the sequencing plan gets its own implementation plan file under `docs/plans/`, paired with this design document.

---

## Implementation Postscript — 2026-05-11

All 19 phases shipped. Key commits: `f1f370f` (`DialogCustomization` builder + `InsertedDialogStep`),
`530f25f` (`RegisterDialogStep` on `ExtensionContext`), `c7a4957` (DLG001 / DLG002 validators),
`dcabf3b` (legacy `SharedDialogBuilders` deleted, templates rewritten). Follow-on layout-DSL
commits landed the full `Layouts.Standard370x270`, `DialogComposer`, and all ten per-dialog
builders.

### RFC-vs-shipped drift

| RFC design | Shipped | Winner |
|-----------|---------|--------|
| `RegionPolicy.RightPacked` as an enum value | Shipped as both: enum value `RightPacked` **and** implementation class `RightPackedRegionLayout` implementing `IRegionLayoutPolicy` | Shipped — strategy pattern gives testable, swappable policies. RFC sketched the enum only. |
| `IDialogStepBuilder` named that | Shipped in `FalkForge.Extensibility` with that exact name | RFC matched exactly. |
| Compiler-side builder contract also `IDialogStepBuilder` | Shipped as `IMsiDialogStepBuilder` (extends `IDialogStepBuilder`) in `FalkForge.Compiler.Msi` — avoids requiring `Compiler.Msi` reference just to name-register a step | Shipped wins — two-level split (marker in Extensibility, full builder in Compiler.Msi) is cleaner. RFC had a single interface. |
| `DialogBuildContext.ForTest(package, customization)` — two args | Shipped as `DialogBuildContext.ForTest(customization)` — one arg; package model not needed since builders receive flow context separately | Shipped wins — simpler test helper. |
| Per-builder classes implement `IDialogStepBuilder` directly | Shipped as internal static classes with `static Build(DialogFlowContext)` methods, not implementing any interface; templates call them directly | Shipped wins — stock builders don't need interface polymorphism; only extension builders do. |
| `RegionDefaults.Gaps` array for cutover gap parity | Not shipped — uniform `Gap = 8` used from day one | RFC never needed this. Cutover was clean enough without per-child gap overrides. |
| 5 templates shrink to 30–50 LOC each | Shipped at 46–89 LOC each | Within expected range; some templates (e.g. `AdvancedDialogTemplate`) are larger due to additional dialogs. |

### Phase 13 (byte-identical cutover tests)

Phase 13 was not implemented. The new DSL produces structurally equivalent output but not
byte-identical output to the legacy `SharedDialogBuilders` — button gaps differ slightly
(uniform 8 DLU vs. the legacy non-uniform hand-placed offsets). Per-builder unit tests
covering coordinate regression, tab-chain integrity, and event wiring provided sufficient
regression coverage during the migration. The legacy path was deleted once per-builder
tests were green, without a byte-identical gate.

### Phase 18 (delete byte-identical cutover tests)

Moot — Phase 13 was never implemented, so no cutover tests existed to delete. No test
files with "cutover", "byte-identical", or "byteidentical" in their names exist in the
test tree. Per-builder unit tests under `tests/FalkForge.Compiler.Msi.Tests/UI/Layout/`
serve as the permanent regression suite.

### Architecture documentation

Phase 19 shipped as `docs/dialog-template-architecture.md` covering the layout DSL,
`Layouts.Standard370x270` region table, per-dialog builder walkthrough, customization
facade, extension step contribution, and DLG001/DLG002 validation.
