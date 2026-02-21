# UI Localization Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add auto-locale localization to the custom UI framework (runtime) and MSI dialog templates (compile-time), with optional end-user language switching and Swedish/English translations for all demos.

**Architecture:** Two localization systems unified by JSON format and culture fallback. Custom UI uses `InstallerPage.Localize("key")` resolving embedded JSON at runtime with `UiStringResolver`. MSI templates use `!(loc.Dialog.X)` references resolved at compile time by `LocalizedStringResolver`. Optional `LanguageSelectorControl` enables runtime language switching via blanket `PropertyChanged("")`.

**Tech Stack:** .NET 10, WPF, xUnit, System.Text.Json, System.Globalization.CultureInfo

---

### Task 1: UiStringResolver — Core Runtime String Resolution

**Files:**
- Create: `src/FalkForge.Ui/Localization/UiStringResolver.cs`
- Test: `tests/FalkForge.Ui.Tests/Localization/UiStringResolverTests.cs`

The `UiStringResolver` holds loaded culture dictionaries and resolves keys with fallback. It reuses the same fallback logic as `CultureFallbackChain` in `FalkForge.Localization`.

**Implementation:**

```csharp
// src/FalkForge.Ui/Localization/UiStringResolver.cs
namespace FalkForge.Ui.Localization;

using FalkForge.Localization;

internal sealed class UiStringResolver
{
    private readonly Dictionary<string, Dictionary<string, string>> _cultures;
    private readonly string _defaultCulture;
    private IReadOnlyList<string> _fallbackChain;

    public UiStringResolver(
        Dictionary<string, Dictionary<string, string>> cultures,
        string defaultCulture)
    {
        _cultures = cultures;
        _defaultCulture = defaultCulture;
        CurrentCulture = defaultCulture;
        _fallbackChain = CultureFallbackChain.Build(defaultCulture, defaultCulture);
    }

    public string CurrentCulture { get; private set; }

    public IReadOnlyCollection<string> AvailableCultures => _cultures.Keys;

    public event Action? CultureChanged;

    public void SetCulture(string culture)
    {
        if (string.Equals(CurrentCulture, culture, StringComparison.OrdinalIgnoreCase))
            return;
        CurrentCulture = culture;
        _fallbackChain = CultureFallbackChain.Build(culture, _defaultCulture);
        CultureChanged?.Invoke();
    }

    public string Resolve(string key)
    {
        foreach (var culture in _fallbackChain)
        {
            if (_cultures.TryGetValue(culture, out var strings) &&
                strings.TryGetValue(key, out var value))
                return value;
        }
        return key;
    }
}
```

**Tests:**

```csharp
// tests/FalkForge.Ui.Tests/Localization/UiStringResolverTests.cs
namespace FalkForge.Ui.Tests.Localization;

using FalkForge.Ui.Localization;
using Xunit;

public class UiStringResolverTests
{
    private static UiStringResolver CreateResolver()
    {
        var cultures = new Dictionary<string, Dictionary<string, string>>
        {
            ["en-US"] = new()
            {
                ["Welcome.Title"] = "Welcome",
                ["Welcome.Subtitle"] = "Click Next to continue"
            },
            ["sv-SE"] = new()
            {
                ["Welcome.Title"] = "Välkommen",
                ["Welcome.Subtitle"] = "Klicka på Nästa för att fortsätta"
            },
            ["sv"] = new()
            {
                ["Welcome.Title"] = "Välkommen"
            }
        };
        return new UiStringResolver(cultures, "en-US");
    }

    [Fact]
    public void Resolve_default_culture_returns_english()
    {
        var resolver = CreateResolver();
        Assert.Equal("Welcome", resolver.Resolve("Welcome.Title"));
    }

    [Fact]
    public void Resolve_after_set_culture_returns_swedish()
    {
        var resolver = CreateResolver();
        resolver.SetCulture("sv-SE");
        Assert.Equal("Välkommen", resolver.Resolve("Welcome.Title"));
    }

    [Fact]
    public void Resolve_missing_key_returns_key()
    {
        var resolver = CreateResolver();
        Assert.Equal("Missing.Key", resolver.Resolve("Missing.Key"));
    }

    [Fact]
    public void Resolve_fallback_sv_SE_to_sv_to_en_US()
    {
        var cultures = new Dictionary<string, Dictionary<string, string>>
        {
            ["en-US"] = new() { ["A"] = "English-A", ["B"] = "English-B", ["C"] = "English-C" },
            ["sv"] = new() { ["A"] = "Swedish-A", ["B"] = "Swedish-B" },
            ["sv-SE"] = new() { ["A"] = "Swedish-SE-A" }
        };
        var resolver = new UiStringResolver(cultures, "en-US");
        resolver.SetCulture("sv-SE");

        Assert.Equal("Swedish-SE-A", resolver.Resolve("A"));
        Assert.Equal("Swedish-B", resolver.Resolve("B"));
        Assert.Equal("English-C", resolver.Resolve("C"));
    }

    [Fact]
    public void SetCulture_fires_CultureChanged()
    {
        var resolver = CreateResolver();
        var fired = false;
        resolver.CultureChanged += () => fired = true;

        resolver.SetCulture("sv-SE");

        Assert.True(fired);
    }

    [Fact]
    public void SetCulture_same_value_does_not_fire()
    {
        var resolver = CreateResolver();
        var fired = false;
        resolver.CultureChanged += () => fired = true;

        resolver.SetCulture("en-US");

        Assert.False(fired);
    }

    [Fact]
    public void AvailableCultures_returns_all_loaded_cultures()
    {
        var resolver = CreateResolver();
        Assert.Contains("en-US", resolver.AvailableCultures);
        Assert.Contains("sv-SE", resolver.AvailableCultures);
        Assert.Contains("sv", resolver.AvailableCultures);
    }

    [Fact]
    public void CurrentCulture_defaults_to_default_culture()
    {
        var resolver = CreateResolver();
        Assert.Equal("en-US", resolver.CurrentCulture);
    }
}
```

**Verify:** `dotnet test tests/FalkForge.Ui.Tests --filter "FullyQualifiedName~UiStringResolverTests"`

**Commit:** `feat: add UiStringResolver for runtime localization`

---

### Task 2: UiLocalizationBuilder + UiLocalizationConfig

**Files:**
- Create: `src/FalkForge.Ui/Localization/UiLocalizationBuilder.cs`
- Create: `src/FalkForge.Ui/Localization/UiLocalizationConfig.cs`
- Test: `tests/FalkForge.Ui.Tests/Localization/UiLocalizationBuilderTests.cs`

The builder loads JSON from embedded resources and builds a `UiStringResolver`. `AddJsonResource<T>` uses `typeof(T).Assembly` to locate embedded resources.

**Implementation:**

```csharp
// src/FalkForge.Ui/Localization/UiLocalizationConfig.cs
namespace FalkForge.Ui.Localization;

internal sealed record UiLocalizationConfig(
    UiStringResolver Resolver,
    bool AllowLanguageSelection);
```

```csharp
// src/FalkForge.Ui/Localization/UiLocalizationBuilder.cs
namespace FalkForge.Ui.Localization;

using System.Globalization;
using System.Reflection;
using System.Text.Json;
using FalkForge.Localization;

public sealed class UiLocalizationBuilder
{
    private readonly List<(Assembly Assembly, string ResourcePath)> _resources = [];
    private string _defaultCulture = "en-US";
    private bool _detectCulture = true;
    private bool _allowLanguageSelection;

    public UiLocalizationBuilder DefaultCulture(string culture)
    {
        _defaultCulture = culture;
        return this;
    }

    public UiLocalizationBuilder AddJsonResource<T>(string path)
    {
        _resources.Add((typeof(T).Assembly, path));
        return this;
    }

    public UiLocalizationBuilder AddJsonResource(Assembly assembly, string path)
    {
        _resources.Add((assembly, path));
        return this;
    }

    public UiLocalizationBuilder DetectCulture(bool detect = true)
    {
        _detectCulture = detect;
        return this;
    }

    public UiLocalizationBuilder AllowLanguageSelection()
    {
        _allowLanguageSelection = true;
        return this;
    }

    internal UiLocalizationConfig Build()
    {
        var cultures = new Dictionary<string, Dictionary<string, string>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var (assembly, path) in _resources)
        {
            var (culture, strings) = LoadEmbeddedResource(assembly, path);
            if (!cultures.TryGetValue(culture, out var existing))
            {
                existing = new Dictionary<string, string>(StringComparer.Ordinal);
                cultures[culture] = existing;
            }
            foreach (var (key, value) in strings)
                existing[key] = value;
        }

        if (cultures.Count == 0)
            throw new InvalidOperationException(
                "No localization resources loaded. Call AddJsonResource() to add culture files.");

        if (!cultures.ContainsKey(_defaultCulture))
            throw new InvalidOperationException(
                $"Default culture '{_defaultCulture}' not found in loaded resources.");

        var resolver = new UiStringResolver(cultures, _defaultCulture);

        if (_detectCulture)
        {
            var uiCulture = CultureInfo.CurrentUICulture.Name;
            if (cultures.ContainsKey(uiCulture) ||
                cultures.ContainsKey(uiCulture.Split('-')[0]))
            {
                resolver.SetCulture(uiCulture);
            }
        }

        return new UiLocalizationConfig(resolver, _allowLanguageSelection);
    }

    private static (string Culture, Dictionary<string, string> Strings) LoadEmbeddedResource(
        Assembly assembly, string path)
    {
        // Convert path separators to dots for embedded resource naming
        var resourceName = path.Replace('/', '.').Replace('\\', '.');

        // Find matching resource name (may be prefixed with default namespace)
        var fullName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));

        if (fullName is null)
            throw new InvalidOperationException(
                $"Embedded resource '{path}' not found in assembly '{assembly.GetName().Name}'. " +
                $"Ensure the file is marked as <EmbeddedResource> in the .csproj.");

        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        var culture = LocalizationLoader.ExtractCultureFromFileName(path)
            ?? throw new InvalidOperationException(
                $"Cannot extract culture from resource path '{path}'. " +
                $"Expected format: name.culture.json (e.g., strings.en-US.json)");

        var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
            ?? throw new InvalidOperationException(
                $"Localization resource '{path}' contains null or invalid JSON.");

        return (culture, raw);
    }
}
```

**Tests:**

```csharp
// tests/FalkForge.Ui.Tests/Localization/UiLocalizationBuilderTests.cs
namespace FalkForge.Ui.Tests.Localization;

using FalkForge.Ui.Localization;
using Xunit;

public class UiLocalizationBuilderTests
{
    [Fact]
    public void Build_no_resources_throws()
    {
        var builder = new UiLocalizationBuilder();
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void Build_missing_default_culture_throws()
    {
        var builder = new UiLocalizationBuilder()
            .DefaultCulture("fr-FR")
            .AddJsonResource<UiLocalizationBuilderTests>("Localization.teststrings.en-US.json");

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void Build_with_embedded_resources_resolves_strings()
    {
        var config = new UiLocalizationBuilder()
            .DefaultCulture("en-US")
            .AddJsonResource<UiLocalizationBuilderTests>("Localization.teststrings.en-US.json")
            .DetectCulture(false)
            .Build();

        Assert.Equal("Test Welcome", config.Resolver.Resolve("Test.Welcome"));
    }

    [Fact]
    public void Build_AllowLanguageSelection_sets_config()
    {
        var config = new UiLocalizationBuilder()
            .DefaultCulture("en-US")
            .AddJsonResource<UiLocalizationBuilderTests>("Localization.teststrings.en-US.json")
            .AllowLanguageSelection()
            .DetectCulture(false)
            .Build();

        Assert.True(config.AllowLanguageSelection);
    }
}
```

**Test data files** (embedded resources in test project):

Create `tests/FalkForge.Ui.Tests/Localization/teststrings.en-US.json`:
```json
{
  "Test.Welcome": "Test Welcome",
  "Test.Next": "Next"
}
```

Create `tests/FalkForge.Ui.Tests/Localization/teststrings.sv-SE.json`:
```json
{
  "Test.Welcome": "Test Välkommen",
  "Test.Next": "Nästa"
}
```

Add to `tests/FalkForge.Ui.Tests/FalkForge.Ui.Tests.csproj`:
```xml
<ItemGroup>
  <EmbeddedResource Include="Localization\teststrings.en-US.json" />
  <EmbeddedResource Include="Localization\teststrings.sv-SE.json" />
</ItemGroup>
```

The `FalkForge.Ui` project needs a reference to `FalkForge.Localization` for `LocalizationLoader.ExtractCultureFromFileName` and `CultureFallbackChain.Build`. Add to `src/FalkForge.Ui/FalkForge.Ui.csproj`:
```xml
<ProjectReference Include="..\FalkForge.Localization\FalkForge.Localization.csproj" />
```

**Verify:** `dotnet test tests/FalkForge.Ui.Tests --filter "FullyQualifiedName~UiLocalizationBuilderTests"`

**Commit:** `feat: add UiLocalizationBuilder for embedded resource loading`

---

### Task 3: Wire Localize() into InstallerPage + InstallerUIBuilder + InstallerApp

**Files:**
- Modify: `src/FalkForge.Ui/InstallerPage.cs`
- Modify: `src/FalkForge.Ui/InstallerUIBuilder.cs`
- Modify: `src/FalkForge.Ui/InstallerApp.cs`
- Test: `tests/FalkForge.Ui.Tests/InstallerPageTests.cs` (add Localize tests)

**InstallerPage changes:**

Add to `InstallerPage.cs`:
```csharp
using FalkForge.Ui.Localization;

// Add field (after existing fields):
internal UiStringResolver? _stringResolver;

// Add methods (after GetPassword):
protected string Localize(string key)
    => _stringResolver?.Resolve(key) ?? key;

internal void NotifyCultureChanged()
    => OnPropertyChanged(string.Empty);
```

**InstallerUIBuilder changes:**

Add `Localization` method and internal field:
```csharp
using FalkForge.Ui.Localization;

// Add field:
private UiLocalizationConfig? _localizationConfig;

// Add method:
public InstallerUIBuilder Localization(Action<UiLocalizationBuilder> configure)
{
    var builder = new UiLocalizationBuilder();
    configure(builder);
    _localizationConfig = builder.Build();
    return this;
}

// Add internal property:
internal UiLocalizationConfig? LocalizationConfig => _localizationConfig;
```

**InstallerApp changes (RunCore method):**

After wiring PluginServices into pages, also wire the string resolver:
```csharp
// After the foreach that sets Engine, SharedState, PluginServices, DetectedState:
if (builder.LocalizationConfig is { } locConfig)
{
    foreach (var page in pages)
        page._stringResolver = locConfig.Resolver;
}
```

Also pass localization config to CustomShellViewModel (for language switching — wired in Task 5).

**Tests (add to InstallerPageTests.cs):**

```csharp
[Fact]
public void Localize_without_resolver_returns_key()
{
    var page = new TestPage();
    Assert.Equal("Some.Key", page.Localize("Some.Key"));
}

[Fact]
public void Localize_with_resolver_returns_resolved_string()
{
    var page = new TestPage();
    var cultures = new Dictionary<string, Dictionary<string, string>>
    {
        ["en-US"] = new() { ["Test.Title"] = "Hello" }
    };
    page._stringResolver = new UiStringResolver(cultures, "en-US");

    Assert.Equal("Hello", page.Localize("Test.Title"));
}
```

Note: `Localize` is `protected`, so the test needs to use a test page that exposes it. Add to `TestPage`:
```csharp
public string TestLocalize(string key) => Localize(key);
```

Then tests use `page.TestLocalize("key")`.

**Verify:** `dotnet test tests/FalkForge.Ui.Tests`

**Commit:** `feat: wire Localize() method into InstallerPage and InstallerUIBuilder`

---

### Task 4: LanguageSelectorControl + Window Integration

**Files:**
- Create: `src/FalkForge.Ui/Localization/LanguageSelectorControl.cs` (code-only, no XAML)
- Modify: `src/FalkForge.Ui/Views/CustomInstallerWindow.xaml`
- Modify: `src/FalkForge.Ui/ViewModels/CustomShellViewModel.cs`
- Modify: `src/FalkForge.Ui/InstallerApp.cs`

**LanguageSelectorControl:**

A simple `ComboBox` subclass that displays available cultures by their native display name.

```csharp
// src/FalkForge.Ui/Localization/LanguageSelectorControl.cs
namespace FalkForge.Ui.Localization;

using System.Globalization;
using System.Windows;
using System.Windows.Controls;

internal sealed class LanguageSelectorControl : ComboBox
{
    private UiStringResolver? _resolver;

    public void Initialize(UiStringResolver resolver)
    {
        _resolver = resolver;
        Items.Clear();

        foreach (var culture in resolver.AvailableCultures.OrderBy(c => c))
        {
            try
            {
                var info = CultureInfo.GetCultureInfo(culture);
                Items.Add(new CultureItem(culture, info.NativeName));
            }
            catch (CultureNotFoundException)
            {
                Items.Add(new CultureItem(culture, culture));
            }
        }

        SelectedItem = Items.Cast<CultureItem>()
            .FirstOrDefault(c => c.Code.Equals(resolver.CurrentCulture, StringComparison.OrdinalIgnoreCase));

        SelectionChanged += OnSelectionChanged;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedItem is CultureItem item)
            _resolver?.SetCulture(item.Code);
    }

    internal sealed record CultureItem(string Code, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}
```

**CustomInstallerWindow.xaml changes:**

Add a `ContentControl` placeholder in the navigation bar area (Row 1, right side before the Next button) where the language selector will be injected:
```xml
<ContentControl x:Name="LanguageSelectorHost"
                Grid.Column="0" HorizontalAlignment="Left"
                Margin="8,0,0,0" VerticalAlignment="Center"/>
```

The exact placement depends on the current grid layout. The language selector should sit in the bottom navigation bar, left-aligned.

**CustomShellViewModel changes:**

Add a property for the language selector control:
```csharp
public FrameworkElement? LanguageSelector { get; private set; }
```

In the constructor or via an init method, if localization config allows language selection:
```csharp
internal void InitializeLocalization(UiLocalizationConfig config)
{
    if (!config.AllowLanguageSelection) return;

    var selector = new LanguageSelectorControl();
    selector.Initialize(config.Resolver);
    LanguageSelector = selector;
    OnPropertyChanged(nameof(LanguageSelector));

    config.Resolver.CultureChanged += () =>
    {
        foreach (var page in _pages)
            page.NotifyCultureChanged();
    };
}
```

**InstallerApp changes:**

After creating CustomShellViewModel, call InitializeLocalization if config exists:
```csharp
if (builder.LocalizationConfig is { } locConfig)
    vm.InitializeLocalization(locConfig);
```

**Verify:** `dotnet build` (WPF controls require build verification, not unit tests for the UI control itself)

**Commit:** `feat: add LanguageSelectorControl with runtime culture switching`

---

### Task 5: Built-in MSI Dialog Localization JSON Files

**Files:**
- Create: `src/FalkForge.Compiler.Msi/Localization/builtin.en-US.json`
- Create: `src/FalkForge.Compiler.Msi/Localization/builtin.sv-SE.json`
- Modify: `src/FalkForge.Compiler.Msi/FalkForge.Compiler.Msi.csproj` (add EmbeddedResource)

Catalog all hardcoded strings from the 5 dialog templates. Key naming: `Dialog.<DialogPart>.<Element>`.

**en-US.json:**
```json
{
  "Dialog.Title": "[ProductName] Setup",
  "Dialog.Welcome.Title": "Welcome to [ProductName]",
  "Dialog.Welcome.Description": "The Setup Wizard will install [ProductName] on your computer. Click Next to continue or Cancel to exit the Setup Wizard.",
  "Dialog.Welcome.DescriptionMinimal": "Click Install to begin the installation.",
  "Dialog.License.Title": "License Agreement",
  "Dialog.License.Description": "Please read the following license agreement.",
  "Dialog.License.Accept": "I &accept the terms in the License Agreement",
  "Dialog.InstallDir.Title": "Destination Folder",
  "Dialog.InstallDir.Description": "Click Next to install to this folder, or click Change to install to a different folder.",
  "Dialog.InstallDir.FolderLabel": "Install [ProductName] to:",
  "Dialog.Customize.Title": "Custom Setup",
  "Dialog.Customize.Description": "Select the features you want to install.",
  "Dialog.SetupType.Title": "Setup Type",
  "Dialog.SetupType.Description": "Choose the setup type that best suits your needs.",
  "Dialog.SetupType.Typical": "&Typical",
  "Dialog.SetupType.TypicalDesc": "Installs the most common program features.",
  "Dialog.SetupType.Custom": "C&ustom",
  "Dialog.SetupType.CustomDesc": "Allows users to choose which features to install.",
  "Dialog.SetupType.Complete": "C&omplete",
  "Dialog.SetupType.CompleteDesc": "All program features will be installed.",
  "Dialog.InstallScope.Title": "Installation Scope",
  "Dialog.InstallScope.Description": "Install for all users or just the current user.",
  "Dialog.InstallScope.PerMachine": "Install for &all users",
  "Dialog.InstallScope.PerMachineDesc": "Requires administrator privileges",
  "Dialog.InstallScope.PerUser": "Install just for &me",
  "Dialog.InstallScope.PerUserDesc": "Does not require administrator privileges",
  "Dialog.Progress.Title": "Installing [ProductName]",
  "Dialog.Progress.Status": "Status:",
  "Dialog.Complete.Title": "Setup Complete",
  "Dialog.Complete.Description": "[ProductName] has been successfully installed.",
  "Dialog.Button.Next": "&Next >",
  "Dialog.Button.Back": "< &Back",
  "Dialog.Button.Cancel": "Cancel",
  "Dialog.Button.Install": "&Install",
  "Dialog.Button.Finish": "&Finish",
  "Dialog.Button.Change": "C&hange...",
  "Dialog.Button.Reset": "&Reset",
  "Dialog.Button.DiskCost": "&Disk Cost..."
}
```

**sv-SE.json:**
```json
{
  "Dialog.Title": "[ProductName] Installation",
  "Dialog.Welcome.Title": "Välkommen till [ProductName]",
  "Dialog.Welcome.Description": "Installationsguiden kommer att installera [ProductName] på din dator. Klicka på Nästa för att fortsätta eller Avbryt för att avsluta.",
  "Dialog.Welcome.DescriptionMinimal": "Klicka på Installera för att påbörja installationen.",
  "Dialog.License.Title": "Licensavtal",
  "Dialog.License.Description": "Vänligen läs följande licensavtal.",
  "Dialog.License.Accept": "Jag &godkänner villkoren i licensavtalet",
  "Dialog.InstallDir.Title": "Målmapp",
  "Dialog.InstallDir.Description": "Klicka på Nästa för att installera i den här mappen, eller klicka på Ändra för att välja en annan mapp.",
  "Dialog.InstallDir.FolderLabel": "Installera [ProductName] till:",
  "Dialog.Customize.Title": "Anpassad installation",
  "Dialog.Customize.Description": "Välj vilka funktioner du vill installera.",
  "Dialog.SetupType.Title": "Installationstyp",
  "Dialog.SetupType.Description": "Välj den installationstyp som passar dig bäst.",
  "Dialog.SetupType.Typical": "&Normal",
  "Dialog.SetupType.TypicalDesc": "Installerar de vanligaste programfunktionerna.",
  "Dialog.SetupType.Custom": "&Anpassad",
  "Dialog.SetupType.CustomDesc": "Låter dig välja vilka funktioner som ska installeras.",
  "Dialog.SetupType.Complete": "&Fullständig",
  "Dialog.SetupType.CompleteDesc": "Alla programfunktioner installeras.",
  "Dialog.InstallScope.Title": "Installationsomfång",
  "Dialog.InstallScope.Description": "Installera för alla användare eller bara den aktuella.",
  "Dialog.InstallScope.PerMachine": "Installera för &alla användare",
  "Dialog.InstallScope.PerMachineDesc": "Kräver administratörsrättigheter",
  "Dialog.InstallScope.PerUser": "Installera bara för &mig",
  "Dialog.InstallScope.PerUserDesc": "Kräver inte administratörsrättigheter",
  "Dialog.Progress.Title": "Installerar [ProductName]",
  "Dialog.Progress.Status": "Status:",
  "Dialog.Complete.Title": "Installationen är klar",
  "Dialog.Complete.Description": "[ProductName] har installerats.",
  "Dialog.Button.Next": "&Nästa >",
  "Dialog.Button.Back": "< &Tillbaka",
  "Dialog.Button.Cancel": "Avbryt",
  "Dialog.Button.Install": "&Installera",
  "Dialog.Button.Finish": "&Slutför",
  "Dialog.Button.Change": "Ä&ndra...",
  "Dialog.Button.Reset": "&Återställ",
  "Dialog.Button.DiskCost": "&Diskutrymme..."
}
```

**csproj change:** Add to `FalkForge.Compiler.Msi.csproj`:
```xml
<ItemGroup>
  <EmbeddedResource Include="Localization\builtin.en-US.json" />
  <EmbeddedResource Include="Localization\builtin.sv-SE.json" />
</ItemGroup>
```

**Verify:** `dotnet build src/FalkForge.Compiler.Msi`

**Commit:** `feat: add built-in en-US and sv-SE localization for MSI dialog templates`

---

### Task 6: AddBuiltInCultures() + DetectCulture() on LocalizationBuilder

**Files:**
- Modify: `src/FalkForge.Localization/LocalizationBuilder.cs`
- Modify: `src/FalkForge.Localization/FalkForge.Localization.csproj` (add Compiler.Msi reference — OR pass assembly from outside)
- Test: `tests/FalkForge.Localization.Tests/` (add tests)

**Approach:** To avoid a circular dependency (Localization depends on Compiler.Msi, and Compiler.Msi already depends on Core), `AddBuiltInCultures()` should accept an assembly parameter, or be an extension method in Compiler.Msi. Best approach: add `AddBuiltInCultures()` as an extension method in `FalkForge.Compiler.Msi` that loads its own embedded resources.

**Create extension method in Compiler.Msi:**

```csharp
// src/FalkForge.Compiler.Msi/BuiltInLocalizationExtensions.cs
namespace FalkForge.Compiler.Msi;

using System.Reflection;
using System.Text.Json;
using FalkForge.Localization;

public static class BuiltInLocalizationExtensions
{
    private static readonly Assembly MsiAssembly = typeof(BuiltInLocalizationExtensions).Assembly;

    public static LocalizationBuilder AddBuiltInCultures(this LocalizationBuilder builder)
    {
        var resources = MsiAssembly.GetManifestResourceNames()
            .Where(n => n.Contains("Localization.builtin.") && n.EndsWith(".json"));

        foreach (var resourceName in resources)
        {
            using var stream = MsiAssembly.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();

            var culture = ExtractCulture(resourceName);
            var strings = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
            builder.AddCulture(culture, strings);
        }

        return builder;
    }

    private static string ExtractCulture(string resourceName)
    {
        // Resource name format: ...Localization.builtin.en-US.json
        var fileName = resourceName.Split('.')[^3..^1]; // ["en-US"] or ["sv-SE"]
        return string.Join("-", fileName);
    }
}
```

Actually, the resource name parsing is tricky. Let me use a simpler approach — hardcode the known culture files:

```csharp
public static LocalizationBuilder AddBuiltInCultures(this LocalizationBuilder builder)
{
    LoadBuiltInCulture(builder, "en-US");
    LoadBuiltInCulture(builder, "sv-SE");
    return builder;
}

private static void LoadBuiltInCulture(LocalizationBuilder builder, string culture)
{
    var resourceName = $"FalkForge.Compiler.Msi.Localization.builtin.{culture}.json";
    using var stream = MsiAssembly.GetManifestResourceStream(resourceName);
    if (stream is null) return;

    using var reader = new StreamReader(stream);
    var json = reader.ReadToEnd();
    var strings = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
    builder.AddCulture(culture, strings);
}
```

**Add DetectCulture() to LocalizationBuilder:**

```csharp
// Add to LocalizationBuilder.cs
using System.Globalization;

// Add field:
private bool _detectCulture;

// Add method:
public LocalizationBuilder DetectCulture()
{
    _detectCulture = true;
    return this;
}

// Modify Build() — after setting _defaultCulture validation, before returning:
// If DetectCulture is enabled, set the default culture to the OS UI culture
// if it's available, otherwise keep the configured default.
// The resolve happens in PackageBuilderExtensions where the culture is selected.
```

Actually, for MSI, `DetectCulture()` should determine which culture to resolve `!(loc.X)` strings with. Currently `PackageBuilderExtensions` uses `_defaultCulture` implicitly (the `LocalizedStringResolver` resolves with the default). We need to pass the detected culture to the resolver.

Simpler approach: `DetectCulture()` on `LocalizationBuilder` modifies `_defaultCulture` to match the OS culture if available:

```csharp
// In Build(), before validation:
if (_detectCulture && _defaultCulture is not null)
{
    var osCulture = CultureInfo.CurrentUICulture.Name;
    // Check if OS culture or its parent is available
    foreach (var (culture, _) in loaded)
    {
        if (culture.Equals(osCulture, StringComparison.OrdinalIgnoreCase) ||
            osCulture.StartsWith(culture + "-", StringComparison.OrdinalIgnoreCase) ||
            culture.StartsWith(osCulture.Split('-')[0], StringComparison.OrdinalIgnoreCase))
        {
            // Don't change _defaultCulture, but the resolver will use this culture
            break;
        }
    }
}
```

Actually, the simplest correct approach: `LocalizedStringResolver.Resolve(input, culture)` already accepts a culture parameter. `PackageBuilderExtensions` just needs to pass the detected culture. Let's add the detected culture to `LocalizationBuilder.Build()` result.

Change the `Build()` return to include the resolved culture:

Add a new field in `LocalizationBuilder`:
```csharp
private string? _resolvedCulture;
```

In `Build()`, after merging all cultures:
```csharp
if (_detectCulture)
{
    var osCulture = CultureInfo.CurrentUICulture.Name;
    _resolvedCulture = FindBestMatch(osCulture, merged.Keys) ?? _defaultCulture;
}
else
{
    _resolvedCulture = _defaultCulture;
}
```

Then `PackageBuilderExtensions` passes this to the resolver.

This is getting complex. Let me simplify: add a `ResolvedCulture` property to `LocalizationBuilder` that `PackageBuilderExtensions` can read after `Build()`. Or change `Build()` to return a tuple/record.

Best: Keep it simple. Add `DetectCulture()` that sets `_defaultCulture` to the detected culture if it's available in the loaded set. This way the existing flow works unchanged — the default culture IS the target culture.

```csharp
// In Build(), right after merging all cultures and before validation:
if (_detectCulture)
{
    var osCulture = CultureInfo.CurrentUICulture.Name;
    if (merged.ContainsKey(osCulture))
    {
        _defaultCulture = osCulture;
    }
    else
    {
        var parent = osCulture.Contains('-') ? osCulture[..osCulture.IndexOf('-')] : null;
        if (parent is not null && merged.ContainsKey(parent))
            _defaultCulture = parent;
    }
}
```

This way if the OS is sv-SE and we have sv-SE strings, the MSI will be compiled with sv-SE as the default culture. If the OS is en-US, it stays en-US. Simple.

**Tests:**

```csharp
// tests/FalkForge.Compiler.Msi.Tests/BuiltInLocalizationExtensionsTests.cs
[Fact]
public void AddBuiltInCultures_loads_en_US_and_sv_SE()
{
    var builder = new LocalizationBuilder();
    builder.AddBuiltInCultures();
    builder.DefaultCulture("en-US");

    var result = builder.Build();

    Assert.True(result.IsSuccess);
    Assert.True(result.Value.Count >= 2);
    Assert.Contains(result.Value, m => m.Culture == "en-US");
    Assert.Contains(result.Value, m => m.Culture == "sv-SE");
}

[Fact]
public void AddBuiltInCultures_contains_dialog_button_next()
{
    var builder = new LocalizationBuilder();
    builder.AddBuiltInCultures();
    builder.DefaultCulture("en-US");

    var result = builder.Build();

    var enUS = result.Value.First(m => m.Culture == "en-US");
    Assert.True(enUS.Strings.ContainsKey("Dialog.Button.Next"));
    Assert.Equal("&Next >", enUS.Strings["Dialog.Button.Next"]);
}
```

**Verify:** `dotnet test tests/FalkForge.Compiler.Msi.Tests --filter "FullyQualifiedName~BuiltInLocalizationExtensionsTests"`

**Commit:** `feat: add AddBuiltInCultures() and DetectCulture() for MSI dialog localization`

---

### Task 7: Update MSI Dialog Templates to Use !(loc.X) References

**Files:**
- Modify: `src/FalkForge.Compiler.Msi/UI/Templates/MinimalDialogTemplate.cs`
- Modify: `src/FalkForge.Compiler.Msi/UI/Templates/InstallDirDialogTemplate.cs`
- Modify: `src/FalkForge.Compiler.Msi/UI/Templates/FeatureTreeDialogTemplate.cs`
- Modify: `src/FalkForge.Compiler.Msi/UI/Templates/MondoDialogTemplate.cs`
- Modify: `src/FalkForge.Compiler.Msi/UI/Templates/AdvancedDialogTemplate.cs`

Replace all hardcoded English strings with `!(loc.Dialog.X)` references matching the keys in the built-in JSON files. Examples:

| Hardcoded | Localized Reference |
|-----------|-------------------|
| `"&Next >"` | `"!(loc.Dialog.Button.Next)"` |
| `"< &Back"` | `"!(loc.Dialog.Button.Back)"` |
| `"Cancel"` | `"!(loc.Dialog.Button.Cancel)"` |
| `"&Install"` | `"!(loc.Dialog.Button.Install)"` |
| `"&Finish"` | `"!(loc.Dialog.Button.Finish)"` |
| `"C&hange..."` | `"!(loc.Dialog.Button.Change)"` |
| `"{\\DlgFontBold8}Welcome to [ProductName]"` | `"{\\DlgFontBold8}!(loc.Dialog.Welcome.Title)"` |
| `"Click Install to begin the installation."` | `"!(loc.Dialog.Welcome.DescriptionMinimal)"` |
| `"[ProductName] Setup"` | `"!(loc.Dialog.Title)"` |
| `"{\\DlgFontBold8}Setup Complete"` | `"{\\DlgFontBold8}!(loc.Dialog.Complete.Title)"` |
| `"[ProductName] has been successfully installed."` | `"!(loc.Dialog.Complete.Description)"` |
| `"{\\DlgFontBold8}Installing [ProductName]"` | `"{\\DlgFontBold8}!(loc.Dialog.Progress.Title)"` |
| `"Status:"` | `"!(loc.Dialog.Progress.Status)"` |
| `"{\\DlgFontBold8}License Agreement"` | `"{\\DlgFontBold8}!(loc.Dialog.License.Title)"` |
| `"Please read the following license agreement."` | `"!(loc.Dialog.License.Description)"` |
| `"I &accept the terms in the License Agreement"` | `"!(loc.Dialog.License.Accept)"` |
| `"{\\DlgFontBold8}Destination Folder"` | `"{\\DlgFontBold8}!(loc.Dialog.InstallDir.Title)"` |
| etc. |

**Important:** Font prefixes like `{\\DlgFontBold8}` should NOT be inside the localization reference. Keep them outside: `"{\\DlgFontBold8}!(loc.Dialog.Welcome.Title)"`. This way the font formatting is template-level and the localized string only contains the text.

Apply this to all 5 templates systematically. Every dialog's Title property (`"[ProductName] Setup"`) becomes `"!(loc.Dialog.Title)"`.

**Verify:** `dotnet build && dotnet test`

The existing tests should still pass because `!(loc.X)` references in templates are resolved later by the compiler when localization data is present, and pass through as-is when no localization is configured (which is the current state of most demos/tests).

**Important consideration:** If no localization is configured, the `!(loc.X)` references will appear literally in the MSI. This means existing demos that DON'T configure localization will break. So this task MUST be coordinated with Task 8 (adding `.Localization(loc => loc.AddBuiltInCultures().DetectCulture())` to all demos) OR the templates need a fallback mechanism.

**Fallback approach:** Instead of breaking existing demos, have the template code check if localization is available. If yes, use `!(loc.X)`. If no, use hardcoded English. This means the templates need access to whether localization was configured.

**Simpler approach:** Make `AddBuiltInCultures()` automatic. Add a default behavior in `PackageBuilder` or the compiler that loads built-in cultures if no localization is explicitly configured. This way all templates can safely use `!(loc.X)` and they'll always resolve.

**Simplest approach:** In the `MsiCompiler`, if the `PackageModel.LocalizationData` is empty but the dialog templates use `!(loc.X)` references, auto-load the built-in en-US strings as fallback. This keeps backward compatibility and makes templates always localizable.

Add to `MsiCompiler` or `TableEmitter` (wherever `!(loc.X)` resolution happens): if no localization data is on the model, load built-in en-US strings as default.

**Verify:** `dotnet build && dotnet test` — all existing tests pass, dialogs show English text when no localization configured.

**Commit:** `feat: update MSI dialog templates to use localization references`

---

### Task 8: Demo 11 (custom-ui-simple) Localization

**Files:**
- Create: `demo/11-custom-ui-simple/lang/strings.en-US.json`
- Create: `demo/11-custom-ui-simple/lang/strings.sv-SE.json`
- Modify: `demo/11-custom-ui-simple/11-custom-ui-simple.csproj` (add EmbeddedResource)
- Modify: `demo/11-custom-ui-simple/Program.cs` (add Localization config)
- Modify: `demo/11-custom-ui-simple/Pages/WelcomePage.cs`
- Modify: `demo/11-custom-ui-simple/Pages/ProgressPage.cs` (if it has localizable strings)
- Modify: `demo/11-custom-ui-simple/Pages/CompletePage.cs`

**en-US.json:**
```json
{
  "Welcome.Title": "Welcome",
  "Welcome.ProductName": "My Application",
  "Welcome.Description": "This wizard will install My Application on your computer.\n\nClick Next to continue.",
  "Progress.Title": "Installing",
  "Progress.StatusText": "Preparing installation...",
  "Progress.Installing": "Installing...",
  "Complete.Title": "Complete",
  "Complete.Message": "My Application has been successfully installed."
}
```

**sv-SE.json:**
```json
{
  "Welcome.Title": "Välkommen",
  "Welcome.ProductName": "Min applikation",
  "Welcome.Description": "Den här guiden installerar Min applikation på din dator.\n\nKlicka på Nästa för att fortsätta.",
  "Progress.Title": "Installerar",
  "Progress.StatusText": "Förbereder installation...",
  "Progress.Installing": "Installerar...",
  "Complete.Title": "Slutfört",
  "Complete.Message": "Min applikation har installerats."
}
```

**Program.cs update:**
```csharp
using FalkForge.Ui;
using CustomUiSimple.Pages;

return InstallerApp.Run(args, app => app
    .Localization(loc => loc
        .DefaultCulture("en-US")
        .AddJsonResource<WelcomePage>("lang.strings.en-US.json")
        .AddJsonResource<WelcomePage>("lang.strings.sv-SE.json")
        .AllowLanguageSelection())
    .Window(w => w
        .Size(500, 350)
        .Title("My App Setup")
        .Accent("#2563EB"))
    .Pages(p => p
        .Add<WelcomePage>()
        .Add<ProgressPage>()
        .Add<CompletePage>()));
```

**WelcomePage.cs update:**
```csharp
public class WelcomePage : InstallerPage<WelcomeView>
{
    public override string Title => Localize("Welcome.Title");
    public string ProductName => Localize("Welcome.ProductName");
    public string Description => Localize("Welcome.Description");

    public override PageResult OnNext() => PageResult.Next;
    public override bool CanGoBack => false;
}
```

**CompletePage.cs update:**
```csharp
public class CompletePage : InstallerPage<CompleteView>
{
    public override string Title => Localize("Complete.Title");
    public string Message => Localize("Complete.Message");

    public override PageResult OnNext() => PageResult.Finish;
    public override bool CanGoBack => false;
}
```

**ProgressPage.cs update:**
```csharp
public class ProgressPage : InstallerPage<ProgressView>
{
    private double _progress;
    private string? _statusTextOverride;

    public override string Title => Localize("Progress.Title");

    public double Progress
    {
        get => _progress;
        set => SetField(ref _progress, value);
    }

    public string StatusText
    {
        get => _statusTextOverride ?? Localize("Progress.StatusText");
        set => SetField(ref _statusTextOverride, value);
    }

    public override PageResult OnNext() => PageResult.Install;
    public override bool CanGoBack => false;
}
```

**csproj update:**
```xml
<ItemGroup>
  <EmbeddedResource Include="lang\strings.en-US.json" />
  <EmbeddedResource Include="lang\strings.sv-SE.json" />
</ItemGroup>
```

**Verify:** `dotnet build demo/11-custom-ui-simple`

**Commit:** `feat: add Swedish/English localization to demo 11 (custom-ui-simple)`

---

### Task 9: Demo 12 (custom-ui-vstyle) Localization

**Files:**
- Create: `demo/12-custom-ui-vstyle/lang/strings.en-US.json`
- Create: `demo/12-custom-ui-vstyle/lang/strings.sv-SE.json`
- Modify: `demo/12-custom-ui-vstyle/12-custom-ui-vstyle.csproj` (add EmbeddedResource)
- Modify: `demo/12-custom-ui-vstyle/Program.cs`
- Modify: `demo/12-custom-ui-vstyle/Pages/ProductPage.cs`
- Modify: `demo/12-custom-ui-vstyle/Pages/WorkloadsPage.cs`
- Modify: `demo/12-custom-ui-vstyle/Pages/ProgressPage.cs`
- Modify: `demo/12-custom-ui-vstyle/Pages/CompletePage.cs`

**en-US.json:** (all user-visible strings from pages + XAML)
```json
{
  "Product.Title": "FalkForge DevTools Suite",
  "Product.ProductName": "FalkForge DevTools Suite 2026",
  "Product.Description": "A comprehensive suite of development tools for building modern .NET applications, web services, and cloud-native solutions.",
  "Product.IncludedWorkloads": "Included workloads:",
  "Workloads.Title": "Workloads",
  "Workloads.Components": "Components",
  "Workloads.SelectPrompt": "Select a workload to see its components",
  "Workloads.TotalSelected": "Total selected:",
  "Workloads.Required": "(required)",
  "Workloads.SelectAtLeastOne": "Please select at least one workload.",
  "Progress.Title": "Installing",
  "Progress.Header": "Installing FalkForge DevTools Suite",
  "Progress.Preparing": "Preparing...",
  "Progress.OverallProgress": "Overall progress",
  "Complete.Title": "Complete",
  "Complete.Message": "FalkForge DevTools Suite has been successfully installed.",
  "Complete.Details": "You can now open FalkForge DevTools from the Start menu."
}
```

**sv-SE.json:**
```json
{
  "Product.Title": "FalkForge DevTools Suite",
  "Product.ProductName": "FalkForge DevTools Suite 2026",
  "Product.Description": "En omfattande uppsättning utvecklingsverktyg för att bygga moderna .NET-applikationer, webbtjänster och molnbaserade lösningar.",
  "Product.IncludedWorkloads": "Inkluderade arbetsbelastningar:",
  "Workloads.Title": "Arbetsbelastningar",
  "Workloads.Components": "Komponenter",
  "Workloads.SelectPrompt": "Välj en arbetsbelastning för att se dess komponenter",
  "Workloads.TotalSelected": "Totalt valt:",
  "Workloads.Required": "(obligatorisk)",
  "Workloads.SelectAtLeastOne": "Välj minst en arbetsbelastning.",
  "Progress.Title": "Installerar",
  "Progress.Header": "Installerar FalkForge DevTools Suite",
  "Progress.Preparing": "Förbereder...",
  "Progress.OverallProgress": "Övergripande förlopp",
  "Complete.Title": "Slutfört",
  "Complete.Message": "FalkForge DevTools Suite har installerats.",
  "Complete.Details": "Du kan nu öppna FalkForge DevTools från Start-menyn."
}
```

Update pages to use `Localize()` for all string properties. Some strings in XAML are hardcoded — move those to properties on the page that use `Localize()` and bind from XAML.

For XAML hardcoded strings (like in ProductView.xaml: "Version", "Included workloads:", etc.), expose them as properties on the page and use `Localize()`. Update XAML bindings accordingly.

**Verify:** `dotnet build demo/12-custom-ui-vstyle`

**Commit:** `feat: add Swedish/English localization to demo 12 (custom-ui-vstyle)`

---

### Task 10: MAS Demo Localization

**Files:**
- Create: `demo/MAS/lang/strings.en-US.json`
- Create: `demo/MAS/lang/strings.sv-SE.json`
- Modify: `demo/MAS/MAS.csproj` (add EmbeddedResource)
- Modify: `demo/MAS/Program.cs`
- Modify all 11 pages in `demo/MAS/Pages/` to use `Localize()`

The MAS demo has ~10 pages (WelcomePage, LicensePage, InstallationTypePage, DatabaseServerPage, ConfirmParametersPage, AdvancedInstallDirMultiServerPage, AdvancedInstallDirMultiServerExPage, DatabaseConnectionSettingsPage, MultiServerAdvancedSettingsPage, MultiServerExAdvancedSettingsPage).

**en-US.json:** Extract all user-visible strings from all pages and the MasPageBase virtual properties. Include titles, subtitles, labels, button text, warning messages, etc.

**sv-SE.json:** Swedish translations for all strings.

**Program.cs update:**
```csharp
return InstallerApp.Run(args, app => app
    .Localization(loc => loc
        .DefaultCulture("en-US")
        .AddJsonResource<WelcomePage>("lang.strings.en-US.json")
        .AddJsonResource<WelcomePage>("lang.strings.sv-SE.json")
        .AllowLanguageSelection())
    .Plugin<SqlPlugin>()
    .Plugin<OdbcPlugin>()
    .Plugin<FileSystemPlugin>()
    .Window(w => w.CustomWindow<MasInstallerWindow>())
    .Pages(p => p
        .Add<WelcomePage>()
        // ... rest of pages
    ));
```

**Verify:** `dotnet build demo/MAS`

**Commit:** `feat: add Swedish/English localization to MAS demo`

---

### Task 11: Demos 01-10 MSI Localization

**Files:**
- Modify: `demo/01-hello-world/Program.cs`
- Modify: `demo/02-notepad-clone/Program.cs`
- Modify: `demo/03-client-server/Program.cs`
- Modify: `demo/04-dev-toolkit/Program.cs`
- Modify: `demo/05-enterprise-suite/Program.cs`
- Modify: `demo/06-product-suite/app-installer/Program.cs`
- Modify: `demo/06-product-suite/service-installer/Program.cs`
- Modify: `demo/07-extensions-showcase/Program.cs`
- Modify: `demo/08-localization/Program.cs`
- Modify: `demo/09-advanced-msi/Program.cs`
- Modify: `demo/10-advanced-bundle/msi-package/Program.cs`

Each demo gets `.Localization(loc => loc.AddBuiltInCultures().DetectCulture())` added to the PackageBuilder chain. This requires adding `using FalkForge.Localization;` and `using FalkForge.Compiler.Msi;` (for the extension method).

**Demo 01 example:**
```csharp
using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Localization;
using FalkForge.Models;

return Installer.Build(args, package =>
{
    package.Name = "Hello World";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.UseDialogSet(MsiDialogSet.Minimal);

    package.Localization(loc => loc
        .AddBuiltInCultures()
        .DefaultCulture("en-US")
        .DetectCulture());

    package.Files(files => files
        .Add("payload/hello.txt")
        .To(KnownFolder.ProgramFiles / "Demo" / "HelloWorld"));

}, new MsiCompiler());
```

**Demo 08** already has localization config — add `.AddBuiltInCultures()` to the existing config:
```csharp
package.Localization(loc =>
{
    loc.AddBuiltInCultures(); // Add built-in dialog strings
    loc.AddJsonFile(Path.Combine(langDir, "strings.en-US.json"));
    // ... existing config
    loc.DefaultCulture("en-US");
});
```

**Verify:** `dotnet build` (all demos)

**Commit:** `feat: add built-in localization to demos 01-10`

---

### Task 12: Update CLAUDE.md + Documentation

**Files:**
- Modify: `CLAUDE.md`
- Modify: `documentation.html`

**CLAUDE.md updates:**
- Add `UiStringResolver`, `UiLocalizationBuilder`, `UiLocalizationConfig`, `LanguageSelectorControl` to FalkForge.Ui section
- Add `BuiltInLocalizationExtensions` to Compiler.Msi section
- Add `DetectCulture()` to Localization section
- Update namespace conventions

**documentation.html updates:**
- Add localization section to Custom UI Framework chapter
- Document `Localize()` method, `UiLocalizationBuilder` API, `AllowLanguageSelection()`
- Document `AddBuiltInCultures()` and `DetectCulture()` for MSI
- Update Appendix with Localize() helper
- Add localization examples

**Verify:** `dotnet build`

**Commit:** `docs: add UI localization to documentation and CLAUDE.md`

---

### Task 13: Final Build + Test Verification

Run full build and test suite:

```bash
dotnet build
dotnet test
```

All tests must pass. Zero warnings.

**Commit:** No commit needed — verification only.
