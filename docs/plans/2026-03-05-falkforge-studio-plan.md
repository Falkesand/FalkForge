# FalkForge Studio MVP — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task.

**Goal:** Build a WPF visual installer builder (MVP) that lets users create MSI packages via a GUI that calls the existing `PackageBuilder` → `MsiCompiler` pipeline.

**Architecture:** WPF application with tree navigation, context-sensitive editors, and a JSON project file (`.ffstudio`). Each editor is a UserControl + ViewModel that maps to `PackageBuilder` API calls. Build pipeline: JSON → `PackageBuilder.Build()` → `PackageModel` → `MsiCompiler.Compile()` → MSI.

**Tech Stack:** C# 13, .NET 10, WPF, MVVM (no framework — hand-rolled INotifyPropertyChanged), System.Text.Json, xUnit

**Design doc:** `docs/plans/2026-03-05-falkforge-studio-design.md`

---

## Task Overview

| # | Task | Scope |
|---|------|-------|
| 1 | Project scaffold + shell window | WPF project, main window, tree nav, editor hosting |
| 2 | Project model + JSON load/save | StudioProject model, serialization, new/open/save |
| 3 | Product editor | Name, manufacturer, version, upgrade code, arch, scope |
| 4 | Files editor | File/folder adding, install directory, source paths |
| 5 | Features editor | Feature tree, file assignment |
| 6 | UI & build settings editor | Dialog set, license, compression, output path |
| 7 | Build service | PackageBuilder mapping, MsiCompiler integration, output pane |
| 8 | Integration test + full verification | End-to-end: load project → build MSI |

---

### Task 1: Project Scaffold + Shell Window

**Files:**
- Create: `src/FalkForge.Studio/FalkForge.Studio.csproj`
- Create: `src/FalkForge.Studio/App.xaml`
- Create: `src/FalkForge.Studio/App.xaml.cs`
- Create: `src/FalkForge.Studio/Shell/StudioWindow.xaml`
- Create: `src/FalkForge.Studio/Shell/StudioWindow.xaml.cs`
- Create: `src/FalkForge.Studio/Shell/StudioViewModel.cs`
- Create: `src/FalkForge.Studio/Navigation/TreeNodeViewModel.cs`
- Create: `src/FalkForge.Studio/ViewModelBase.cs`
- Create: `tests/FalkForge.Studio.Tests/FalkForge.Studio.Tests.csproj`
- Create: `tests/FalkForge.Studio.Tests/Navigation/TreeNodeViewModelTests.cs`
- Modify: `FalkForge.slnx` — add both projects

**Context:**
This is the application skeleton. The main window has three areas: tree navigation (left), editor host (center), output pane (bottom). The tree shows installer sections (Product, Files, Features, UI, Build Settings). Clicking a tree node swaps the editor in the center area. No actual editors yet — just the shell and navigation.

Existing MVVM reference: `demo/12-custom-ui-vstyle/` uses `InstallerPage<TView>` pattern. Studio uses a simpler approach — direct `ContentControl` with `DataTemplate` selection.

**Step 1:** Create the csproj files

`src/FalkForge.Studio/FalkForge.Studio.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <UseWPF>true</UseWPF>
    <RootNamespace>FalkForge.Studio</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\FalkForge.Core\FalkForge.Core.csproj" />
    <ProjectReference Include="..\FalkForge.Compiler\FalkForge.Compiler.csproj" />
  </ItemGroup>
</Project>
```

`tests/FalkForge.Studio.Tests/FalkForge.Studio.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <UseWPF>true</UseWPF>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="*" />
    <PackageReference Include="xunit" Version="*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="*" />
    <PackageReference Include="NSubstitute" Version="*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\FalkForge.Studio\FalkForge.Studio.csproj" />
  </ItemGroup>
</Project>
```

Add both to `FalkForge.slnx`.

**Step 2:** Create ViewModelBase

`src/FalkForge.Studio/ViewModelBase.cs`:
```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FalkForge.Studio;

public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
```

**Step 3:** Create TreeNodeViewModel

`src/FalkForge.Studio/Navigation/TreeNodeViewModel.cs`:
```csharp
using System.Collections.ObjectModel;

namespace FalkForge.Studio.Navigation;

public sealed class TreeNodeViewModel : ViewModelBase
{
    private bool _isSelected;
    private bool _isExpanded;

    public string Label { get; }
    public string NodeKey { get; }
    public ObservableCollection<TreeNodeViewModel> Children { get; } = [];

    public TreeNodeViewModel(string label, string nodeKey)
    {
        Label = label;
        NodeKey = nodeKey;
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
}
```

**Step 4:** Write TreeNodeViewModel tests

`tests/FalkForge.Studio.Tests/Navigation/TreeNodeViewModelTests.cs`:
```csharp
using FalkForge.Studio.Navigation;

namespace FalkForge.Studio.Tests.Navigation;

public class TreeNodeViewModelTests
{
    [Fact]
    public void Constructor_SetsLabelAndKey()
    {
        var node = new TreeNodeViewModel("Product", "product");
        Assert.Equal("Product", node.Label);
        Assert.Equal("product", node.NodeKey);
    }

    [Fact]
    public void IsSelected_RaisesPropertyChanged()
    {
        var node = new TreeNodeViewModel("Test", "test");
        var raised = false;
        node.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TreeNodeViewModel.IsSelected))
                raised = true;
        };
        node.IsSelected = true;
        Assert.True(raised);
        Assert.True(node.IsSelected);
    }

    [Fact]
    public void Children_CanAddNodes()
    {
        var parent = new TreeNodeViewModel("Root", "root");
        parent.Children.Add(new TreeNodeViewModel("Child", "child"));
        Assert.Single(parent.Children);
    }
}
```

**Step 5:** Create StudioViewModel

`src/FalkForge.Studio/Shell/StudioViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using FalkForge.Studio.Navigation;

namespace FalkForge.Studio.Shell;

public sealed class StudioViewModel : ViewModelBase
{
    private ViewModelBase? _currentEditor;
    private string _outputText = string.Empty;
    private string _title = "FalkForge Studio";

    public ObservableCollection<TreeNodeViewModel> TreeNodes { get; } = [];

    public ViewModelBase? CurrentEditor
    {
        get => _currentEditor;
        set => SetProperty(ref _currentEditor, value);
    }

    public string OutputText
    {
        get => _outputText;
        set => SetProperty(ref _outputText, value);
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public StudioViewModel()
    {
        BuildDefaultTree();
    }

    private void BuildDefaultTree()
    {
        TreeNodes.Add(new TreeNodeViewModel("Product", "product") { IsExpanded = true });
        TreeNodes.Add(new TreeNodeViewModel("Files", "files"));
        TreeNodes.Add(new TreeNodeViewModel("Features", "features"));
        TreeNodes.Add(new TreeNodeViewModel("UI & Dialogs", "ui"));
        TreeNodes.Add(new TreeNodeViewModel("Build Settings", "build"));
    }

    public void NavigateTo(string nodeKey)
    {
        // Editors will be wired in later tasks
        OutputText = $"Selected: {nodeKey}";
    }
}
```

**Step 6:** Create StudioWindow XAML

`src/FalkForge.Studio/Shell/StudioWindow.xaml`:
```xml
<Window x:Class="FalkForge.Studio.Shell.StudioWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:nav="clr-namespace:FalkForge.Studio.Navigation"
        Title="{Binding Title}" Height="700" Width="1100"
        WindowStartupLocation="CenterScreen">
    <DockPanel>
        <!-- Menu -->
        <Menu DockPanel.Dock="Top">
            <MenuItem Header="_File">
                <MenuItem Header="_New Project" Click="NewProject_Click" />
                <MenuItem Header="_Open..." Click="OpenProject_Click" />
                <MenuItem Header="_Save" Click="SaveProject_Click" />
                <Separator />
                <MenuItem Header="E_xit" Click="Exit_Click" />
            </MenuItem>
            <MenuItem Header="_Build">
                <MenuItem Header="_Build MSI" Click="Build_Click" />
            </MenuItem>
        </Menu>

        <!-- Output Pane -->
        <Border DockPanel.Dock="Bottom" Height="120" BorderBrush="Gray" BorderThickness="0,1,0,0">
            <DockPanel>
                <TextBlock DockPanel.Dock="Top" Text="Output" FontWeight="Bold" Margin="4,2" />
                <TextBox Text="{Binding OutputText, Mode=OneWay}" IsReadOnly="True"
                         VerticalScrollBarVisibility="Auto" FontFamily="Consolas" FontSize="12"
                         BorderThickness="0" />
            </DockPanel>
        </Border>

        <!-- Tree Nav + Editor -->
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="220" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>

            <!-- Tree Navigation -->
            <TreeView Grid.Column="0" ItemsSource="{Binding TreeNodes}"
                      SelectedItemChanged="TreeView_SelectedItemChanged">
                <TreeView.ItemTemplate>
                    <HierarchicalDataTemplate DataType="{x:Type nav:TreeNodeViewModel}"
                                              ItemsSource="{Binding Children}">
                        <TextBlock Text="{Binding Label}" />
                    </HierarchicalDataTemplate>
                </TreeView.ItemTemplate>
                <TreeView.ItemContainerStyle>
                    <Style TargetType="TreeViewItem">
                        <Setter Property="IsSelected" Value="{Binding IsSelected, Mode=TwoWay}" />
                        <Setter Property="IsExpanded" Value="{Binding IsExpanded, Mode=TwoWay}" />
                    </Style>
                </TreeView.ItemContainerStyle>
            </TreeView>

            <GridSplitter Grid.Column="1" Width="4" HorizontalAlignment="Center"
                          VerticalAlignment="Stretch" />

            <!-- Editor Host -->
            <ContentControl Grid.Column="2" Content="{Binding CurrentEditor}" Margin="8">
                <ContentControl.Resources>
                    <!-- DataTemplates for editors will be registered here in later tasks -->
                </ContentControl.Resources>
            </ContentControl>
        </Grid>
    </DockPanel>
</Window>
```

`src/FalkForge.Studio/Shell/StudioWindow.xaml.cs`:
```csharp
using System.Windows;
using System.Windows.Controls;
using FalkForge.Studio.Navigation;

namespace FalkForge.Studio.Shell;

public partial class StudioWindow : Window
{
    private StudioViewModel ViewModel => (StudioViewModel)DataContext;

    public StudioWindow()
    {
        InitializeComponent();
        DataContext = new StudioViewModel();
    }

    private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeNodeViewModel node)
            ViewModel.NavigateTo(node.NodeKey);
    }

    private void NewProject_Click(object sender, RoutedEventArgs e)
        => ViewModel.OutputText = "New project created.";

    private void OpenProject_Click(object sender, RoutedEventArgs e) { }
    private void SaveProject_Click(object sender, RoutedEventArgs e) { }
    private void Build_Click(object sender, RoutedEventArgs e) { }
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}
```

**Step 7:** Create App.xaml

`src/FalkForge.Studio/App.xaml`:
```xml
<Application x:Class="FalkForge.Studio.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="Shell/StudioWindow.xaml">
    <Application.Resources />
</Application>
```

`src/FalkForge.Studio/App.xaml.cs`:
```csharp
using System.Windows;

namespace FalkForge.Studio;

public partial class App : Application
{
}
```

**Step 8:** Verify build

Run: `dotnet build D:/Git/FalkInstaller/src/FalkForge.Studio/FalkForge.Studio.csproj`
Expected: Build succeeded

Run: `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Studio.Tests/FalkForge.Studio.Tests.csproj`
Expected: 3 tests pass

**Step 9:** Commit

```
feat(studio): scaffold WPF project with shell window and tree navigation
```

---

### Task 2: Project Model + JSON Load/Save

**Files:**
- Create: `src/FalkForge.Studio/Project/StudioProject.cs`
- Create: `src/FalkForge.Studio/Project/StudioProjectLoader.cs`
- Create: `tests/FalkForge.Studio.Tests/Project/StudioProjectLoaderTests.cs`

**Context:**
The `StudioProject` is the serializable data model for `.ffstudio` files. It mirrors the `InstallerConfig` structure from `FalkForge.Cli.Models` but is owned by Studio. `StudioProjectLoader` handles JSON round-trip. The JSON format is designed to be human-readable and editable.

Key difference from `InstallerConfig`: Studio project includes build settings (output path, compression) and is the canonical format — not just a CLI config.

**Step 1:** Write StudioProject model

`src/FalkForge.Studio/Project/StudioProject.cs`:
```csharp
using System.Text.Json.Serialization;

namespace FalkForge.Studio.Project;

public sealed class StudioProject
{
    [JsonPropertyName("projectType")]
    public string ProjectType { get; set; } = "msi";

    [JsonPropertyName("product")]
    public ProductSection Product { get; set; } = new();

    [JsonPropertyName("installDirectory")]
    public string? InstallDirectory { get; set; }

    [JsonPropertyName("features")]
    public List<FeatureSection> Features { get; set; } = [];

    [JsonPropertyName("ui")]
    public UiSection Ui { get; set; } = new();

    [JsonPropertyName("build")]
    public BuildSection Build { get; set; } = new();
}

public sealed class ProductSection
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "My Application";

    [JsonPropertyName("manufacturer")]
    public string Manufacturer { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("upgradeCode")]
    public string? UpgradeCode { get; set; }

    [JsonPropertyName("architecture")]
    public string Architecture { get; set; } = "x64";

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = "perMachine";

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public sealed class FeatureSection
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; set; } = true;

    [JsonPropertyName("isRequired")]
    public bool IsRequired { get; set; }

    [JsonPropertyName("files")]
    public List<FileEntry> Files { get; set; } = [];

    [JsonPropertyName("features")]
    public List<FeatureSection>? Features { get; set; }
}

public sealed class FileEntry
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    [JsonPropertyName("targetDirectory")]
    public string? TargetDirectory { get; set; }
}

public sealed class UiSection
{
    [JsonPropertyName("dialogSet")]
    public string DialogSet { get; set; } = "Minimal";

    [JsonPropertyName("licenseFile")]
    public string? LicenseFile { get; set; }
}

public sealed class BuildSection
{
    [JsonPropertyName("outputPath")]
    public string OutputPath { get; set; } = "out/";

    [JsonPropertyName("compression")]
    public string Compression { get; set; } = "High";
}
```

**Step 2:** Write StudioProjectLoader

`src/FalkForge.Studio/Project/StudioProjectLoader.cs`:
```csharp
using System.IO;
using System.Text.Json;

namespace FalkForge.Studio.Project;

public static class StudioProjectLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static StudioProject NewProject()
    {
        return new StudioProject
        {
            Product = new ProductSection
            {
                UpgradeCode = Guid.NewGuid().ToString()
            },
            Features =
            [
                new FeatureSection { Id = "Main", Title = "Main Application", IsDefault = true, IsRequired = true }
            ]
        };
    }

    public static StudioProject LoadFromFile(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<StudioProject>(json, JsonOptions)
               ?? throw new InvalidOperationException($"Failed to deserialize project file: {filePath}");
    }

    public static void SaveToFile(StudioProject project, string filePath)
    {
        var json = JsonSerializer.Serialize(project, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    public static string Serialize(StudioProject project)
        => JsonSerializer.Serialize(project, JsonOptions);

    public static StudioProject Deserialize(string json)
        => JsonSerializer.Deserialize<StudioProject>(json, JsonOptions)
           ?? throw new InvalidOperationException("Failed to deserialize project JSON.");
}
```

**Step 3:** Write tests

`tests/FalkForge.Studio.Tests/Project/StudioProjectLoaderTests.cs`:
```csharp
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Tests.Project;

public class StudioProjectLoaderTests
{
    [Fact]
    public void NewProject_HasDefaults()
    {
        var project = StudioProjectLoader.NewProject();
        Assert.Equal("msi", project.ProjectType);
        Assert.Equal("My Application", project.Product.Name);
        Assert.NotNull(project.Product.UpgradeCode);
        Assert.Single(project.Features);
        Assert.Equal("Main", project.Features[0].Id);
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var project = new StudioProject
        {
            ProjectType = "msi",
            Product = new ProductSection
            {
                Name = "TestApp",
                Manufacturer = "TestCorp",
                Version = "2.0.0",
                UpgradeCode = "12345678-1234-1234-1234-123456789012",
                Architecture = "x86",
                Scope = "perUser",
                Description = "A test application"
            },
            InstallDirectory = "TestCorp/TestApp",
            Features =
            [
                new FeatureSection
                {
                    Id = "Core",
                    Title = "Core Files",
                    IsDefault = true,
                    IsRequired = true,
                    Files = [new FileEntry { Source = "bin/*.dll", TargetDirectory = "bin" }]
                }
            ],
            Ui = new UiSection { DialogSet = "FeatureTree", LicenseFile = "license.rtf" },
            Build = new BuildSection { OutputPath = "dist/", Compression = "Medium" }
        };

        var json = StudioProjectLoader.Serialize(project);
        var loaded = StudioProjectLoader.Deserialize(json);

        Assert.Equal("TestApp", loaded.Product.Name);
        Assert.Equal("TestCorp", loaded.Product.Manufacturer);
        Assert.Equal("2.0.0", loaded.Product.Version);
        Assert.Equal("x86", loaded.Product.Architecture);
        Assert.Equal("perUser", loaded.Product.Scope);
        Assert.Equal("A test application", loaded.Product.Description);
        Assert.Equal("TestCorp/TestApp", loaded.InstallDirectory);
        Assert.Single(loaded.Features);
        Assert.Equal("Core", loaded.Features[0].Id);
        Assert.Single(loaded.Features[0].Files);
        Assert.Equal("bin/*.dll", loaded.Features[0].Files[0].Source);
        Assert.Equal("FeatureTree", loaded.Ui.DialogSet);
        Assert.Equal("license.rtf", loaded.Ui.LicenseFile);
        Assert.Equal("dist/", loaded.Build.OutputPath);
        Assert.Equal("Medium", loaded.Build.Compression);
    }

    [Fact]
    public void RoundTrip_File_PreservesContent()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "FileTest";

        var tempFile = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.ffstudio");
        try
        {
            StudioProjectLoader.SaveToFile(project, tempFile);
            var loaded = StudioProjectLoader.LoadFromFile(tempFile);
            Assert.Equal("FileTest", loaded.Product.Name);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void NestedFeatures_RoundTrip()
    {
        var project = StudioProjectLoader.NewProject();
        project.Features =
        [
            new FeatureSection
            {
                Id = "Root", Title = "Root",
                Features =
                [
                    new FeatureSection { Id = "Child", Title = "Child Feature" }
                ]
            }
        ];

        var json = StudioProjectLoader.Serialize(project);
        var loaded = StudioProjectLoader.Deserialize(json);
        Assert.NotNull(loaded.Features[0].Features);
        Assert.Single(loaded.Features[0].Features!);
        Assert.Equal("Child", loaded.Features[0].Features![0].Id);
    }
}
```

**Step 4:** Verify

Run: `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Studio.Tests/FalkForge.Studio.Tests.csproj`
Expected: 7 tests pass (3 from Task 1 + 4 new)

**Step 5:** Commit

```
feat(studio): add project model and JSON load/save
```

---

### Task 3: Product Editor

**Files:**
- Create: `src/FalkForge.Studio/Editors/ProductEditor/ProductEditorViewModel.cs`
- Create: `src/FalkForge.Studio/Editors/ProductEditor/ProductEditorView.xaml`
- Create: `src/FalkForge.Studio/Editors/ProductEditor/ProductEditorView.xaml.cs`
- Modify: `src/FalkForge.Studio/Shell/StudioViewModel.cs` — wire editor to tree node
- Modify: `src/FalkForge.Studio/Shell/StudioWindow.xaml` — add DataTemplate
- Create: `tests/FalkForge.Studio.Tests/Editors/ProductEditorViewModelTests.cs`

**Context:**
The product editor is a simple form with text fields and dropdowns. It binds to a `ProductSection` from the `StudioProject`. The ViewModel wraps the model with change notification and validation. Architecture dropdown: X86, X64, Arm64. Scope dropdown: PerMachine, PerUser.

**Step 1:** Create ProductEditorViewModel

`src/FalkForge.Studio/Editors/ProductEditor/ProductEditorViewModel.cs`:
```csharp
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.ProductEditor;

public sealed class ProductEditorViewModel : ViewModelBase
{
    private readonly ProductSection _model;

    public ProductEditorViewModel(ProductSection model)
    {
        _model = model;
    }

    public string Name
    {
        get => _model.Name;
        set { _model.Name = value; OnPropertyChanged(); }
    }

    public string Manufacturer
    {
        get => _model.Manufacturer;
        set { _model.Manufacturer = value; OnPropertyChanged(); }
    }

    public string Version
    {
        get => _model.Version;
        set { _model.Version = value; OnPropertyChanged(); }
    }

    public string? UpgradeCode
    {
        get => _model.UpgradeCode;
        set { _model.UpgradeCode = value; OnPropertyChanged(); }
    }

    public string Architecture
    {
        get => _model.Architecture;
        set { _model.Architecture = value; OnPropertyChanged(); }
    }

    public string Scope
    {
        get => _model.Scope;
        set { _model.Scope = value; OnPropertyChanged(); }
    }

    public string? Description
    {
        get => _model.Description;
        set { _model.Description = value; OnPropertyChanged(); }
    }

    public string[] Architectures { get; } = ["x86", "x64", "arm64"];
    public string[] Scopes { get; } = ["perMachine", "perUser"];

    public string? ValidationError
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name)) return "Product name is required.";
            if (string.IsNullOrWhiteSpace(Manufacturer)) return "Manufacturer is required.";
            if (!System.Version.TryParse(Version, out _)) return "Invalid version format.";
            if (UpgradeCode is not null && !Guid.TryParse(UpgradeCode, out _)) return "Invalid GUID format.";
            return null;
        }
    }
}
```

**Step 2:** Write tests

`tests/FalkForge.Studio.Tests/Editors/ProductEditorViewModelTests.cs`:
```csharp
using FalkForge.Studio.Editors.ProductEditor;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Tests.Editors;

public class ProductEditorViewModelTests
{
    [Fact]
    public void Properties_ReadFromModel()
    {
        var model = new ProductSection { Name = "Test", Manufacturer = "Corp", Version = "1.0.0" };
        var vm = new ProductEditorViewModel(model);
        Assert.Equal("Test", vm.Name);
        Assert.Equal("Corp", vm.Manufacturer);
        Assert.Equal("1.0.0", vm.Version);
    }

    [Fact]
    public void SetName_UpdatesModel()
    {
        var model = new ProductSection();
        var vm = new ProductEditorViewModel(model);
        vm.Name = "Updated";
        Assert.Equal("Updated", model.Name);
    }

    [Fact]
    public void SetName_RaisesPropertyChanged()
    {
        var model = new ProductSection();
        var vm = new ProductEditorViewModel(model);
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProductEditorViewModel.Name))
                raised = true;
        };
        vm.Name = "New";
        Assert.True(raised);
    }

    [Fact]
    public void ValidationError_MissingName_ReturnsError()
    {
        var model = new ProductSection { Name = "", Manufacturer = "Corp" };
        var vm = new ProductEditorViewModel(model);
        Assert.NotNull(vm.ValidationError);
        Assert.Contains("name", vm.ValidationError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidationError_InvalidVersion_ReturnsError()
    {
        var model = new ProductSection { Name = "Test", Manufacturer = "Corp", Version = "not.a.version" };
        var vm = new ProductEditorViewModel(model);
        Assert.NotNull(vm.ValidationError);
        Assert.Contains("version", vm.ValidationError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidationError_ValidFields_ReturnsNull()
    {
        var model = new ProductSection { Name = "Test", Manufacturer = "Corp", Version = "1.0.0" };
        var vm = new ProductEditorViewModel(model);
        Assert.Null(vm.ValidationError);
    }

    [Fact]
    public void ValidationError_InvalidGuid_ReturnsError()
    {
        var model = new ProductSection { Name = "Test", Manufacturer = "Corp", Version = "1.0.0", UpgradeCode = "not-a-guid" };
        var vm = new ProductEditorViewModel(model);
        Assert.NotNull(vm.ValidationError);
        Assert.Contains("GUID", vm.ValidationError, StringComparison.OrdinalIgnoreCase);
    }
}
```

**Step 3:** Create ProductEditorView XAML

`src/FalkForge.Studio/Editors/ProductEditor/ProductEditorView.xaml`:
```xml
<UserControl x:Class="FalkForge.Studio.Editors.ProductEditor.ProductEditorView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel Margin="8" MaxWidth="500">
            <TextBlock Text="Product Information" FontSize="18" FontWeight="Bold" Margin="0,0,0,16" />

            <TextBlock Text="Product Name *" FontWeight="SemiBold" Margin="0,0,0,4" />
            <TextBox Text="{Binding Name, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,12" />

            <TextBlock Text="Manufacturer *" FontWeight="SemiBold" Margin="0,0,0,4" />
            <TextBox Text="{Binding Manufacturer, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,12" />

            <TextBlock Text="Version *" FontWeight="SemiBold" Margin="0,0,0,4" />
            <TextBox Text="{Binding Version, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,12" />

            <TextBlock Text="Upgrade Code" FontWeight="SemiBold" Margin="0,0,0,4" />
            <TextBox Text="{Binding UpgradeCode, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,12"
                     FontFamily="Consolas" />

            <TextBlock Text="Architecture" FontWeight="SemiBold" Margin="0,0,0,4" />
            <ComboBox ItemsSource="{Binding Architectures}" SelectedItem="{Binding Architecture}"
                      Margin="0,0,0,12" />

            <TextBlock Text="Install Scope" FontWeight="SemiBold" Margin="0,0,0,4" />
            <ComboBox ItemsSource="{Binding Scopes}" SelectedItem="{Binding Scope}"
                      Margin="0,0,0,12" />

            <TextBlock Text="Description" FontWeight="SemiBold" Margin="0,0,0,4" />
            <TextBox Text="{Binding Description, UpdateSourceTrigger=PropertyChanged}"
                     AcceptsReturn="True" Height="60" TextWrapping="Wrap" Margin="0,0,0,12" />

            <TextBlock Text="{Binding ValidationError}" Foreground="Red" FontStyle="Italic"
                       Visibility="{Binding ValidationError, Converter={StaticResource NullToCollapsedConverter}}" />
        </StackPanel>
    </ScrollViewer>
</UserControl>
```

`src/FalkForge.Studio/Editors/ProductEditor/ProductEditorView.xaml.cs`:
```csharp
using System.Windows.Controls;

namespace FalkForge.Studio.Editors.ProductEditor;

public partial class ProductEditorView : UserControl
{
    public ProductEditorView()
    {
        InitializeComponent();
    }
}
```

**Step 4:** Create NullToCollapsedConverter and wire DataTemplate

Create `src/FalkForge.Studio/Converters/NullToCollapsedConverter.cs`:
```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FalkForge.Studio.Converters;

public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

**Step 5:** Wire editor into StudioViewModel

Update `StudioViewModel.cs` to hold a project and create editors:

Add to StudioViewModel:
```csharp
private StudioProject _project;

public StudioViewModel()
{
    _project = StudioProjectLoader.NewProject();
    BuildDefaultTree();
}

public void NavigateTo(string nodeKey)
{
    CurrentEditor = nodeKey switch
    {
        "product" => new ProductEditorViewModel(_project.Product),
        _ => null
    };
}
```

Update `StudioWindow.xaml` to register `DataTemplate`:
```xml
<ContentControl.Resources>
    <DataTemplate DataType="{x:Type productEditor:ProductEditorViewModel}">
        <productEditor:ProductEditorView />
    </DataTemplate>
</ContentControl.Resources>
```

Add namespace: `xmlns:productEditor="clr-namespace:FalkForge.Studio.Editors.ProductEditor"`

Register converter in `App.xaml`:
```xml
<Application.Resources>
    <converters:NullToCollapsedConverter x:Key="NullToCollapsedConverter" />
</Application.Resources>
```

Add namespace: `xmlns:converters="clr-namespace:FalkForge.Studio.Converters"`

**Step 6:** Verify

Run: `dotnet build D:/Git/FalkInstaller/src/FalkForge.Studio/FalkForge.Studio.csproj`
Expected: Build succeeded

Run: `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Studio.Tests/FalkForge.Studio.Tests.csproj`
Expected: 14 tests pass (7 prior + 7 new)

**Step 7:** Commit

```
feat(studio): add product editor with validation
```

---

### Task 4: Files Editor

**Files:**
- Create: `src/FalkForge.Studio/Editors/FilesEditor/FilesEditorViewModel.cs`
- Create: `src/FalkForge.Studio/Editors/FilesEditor/FilesEditorView.xaml`
- Create: `src/FalkForge.Studio/Editors/FilesEditor/FilesEditorView.xaml.cs`
- Modify: `src/FalkForge.Studio/Shell/StudioViewModel.cs` — wire files editor
- Modify: `src/FalkForge.Studio/Shell/StudioWindow.xaml` — add DataTemplate
- Create: `tests/FalkForge.Studio.Tests/Editors/FilesEditorViewModelTests.cs`

**Context:**
The files editor manages file entries across all features. It shows a list of files with source path and target directory. Users add files via a file browser dialog or by typing paths. Files can use glob patterns (e.g., `bin/*.dll`). Each file can optionally specify a target directory override.

The editor works on the `StudioProject.Features[].Files` collections. For MVP, we present a flat list of all files from all features, with a feature column showing which feature owns each file.

**Step 1:** Create FilesEditorViewModel

`src/FalkForge.Studio/Editors/FilesEditor/FilesEditorViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.FilesEditor;

public sealed class FileEntryViewModel : ViewModelBase
{
    private readonly FileEntry _model;
    private string _featureId;

    public FileEntryViewModel(FileEntry model, string featureId)
    {
        _model = model;
        _featureId = featureId;
    }

    public FileEntry Model => _model;

    public string Source
    {
        get => _model.Source;
        set { _model.Source = value; OnPropertyChanged(); }
    }

    public string? TargetDirectory
    {
        get => _model.TargetDirectory;
        set { _model.TargetDirectory = value; OnPropertyChanged(); }
    }

    public string FeatureId
    {
        get => _featureId;
        set => SetProperty(ref _featureId, value);
    }
}

public sealed class FilesEditorViewModel : ViewModelBase
{
    private readonly StudioProject _project;
    private FileEntryViewModel? _selectedFile;

    public ObservableCollection<FileEntryViewModel> Files { get; } = [];

    public FileEntryViewModel? SelectedFile
    {
        get => _selectedFile;
        set => SetProperty(ref _selectedFile, value);
    }

    public FilesEditorViewModel(StudioProject project)
    {
        _project = project;
        LoadFiles();
    }

    private void LoadFiles()
    {
        Files.Clear();
        foreach (var feature in _project.Features)
            foreach (var file in feature.Files)
                Files.Add(new FileEntryViewModel(file, feature.Id));
    }

    public void AddFile(string source, string featureId)
    {
        var feature = _project.Features.Find(f => f.Id == featureId);
        if (feature is null) return;

        var entry = new FileEntry { Source = source };
        feature.Files.Add(entry);
        var vm = new FileEntryViewModel(entry, featureId);
        Files.Add(vm);
        SelectedFile = vm;
    }

    public void RemoveSelected()
    {
        if (SelectedFile is null) return;

        var feature = _project.Features.Find(f => f.Id == SelectedFile.FeatureId);
        feature?.Files.Remove(SelectedFile.Model);
        Files.Remove(SelectedFile);
        SelectedFile = null;
    }
}
```

**Step 2:** Write tests

`tests/FalkForge.Studio.Tests/Editors/FilesEditorViewModelTests.cs`:
```csharp
using FalkForge.Studio.Editors.FilesEditor;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Tests.Editors;

public class FilesEditorViewModelTests
{
    private static StudioProject CreateProject()
    {
        var project = StudioProjectLoader.NewProject();
        project.Features[0].Files.Add(new FileEntry { Source = "app.exe" });
        project.Features[0].Files.Add(new FileEntry { Source = "lib.dll" });
        return project;
    }

    [Fact]
    public void Constructor_LoadsFilesFromProject()
    {
        var project = CreateProject();
        var vm = new FilesEditorViewModel(project);
        Assert.Equal(2, vm.Files.Count);
        Assert.Equal("app.exe", vm.Files[0].Source);
        Assert.Equal("lib.dll", vm.Files[1].Source);
    }

    [Fact]
    public void AddFile_AddsToCollectionAndModel()
    {
        var project = CreateProject();
        var vm = new FilesEditorViewModel(project);
        vm.AddFile("new.dll", "Main");
        Assert.Equal(3, vm.Files.Count);
        Assert.Equal(3, project.Features[0].Files.Count);
        Assert.Equal("new.dll", vm.Files[2].Source);
    }

    [Fact]
    public void RemoveSelected_RemovesFromCollectionAndModel()
    {
        var project = CreateProject();
        var vm = new FilesEditorViewModel(project);
        vm.SelectedFile = vm.Files[0];
        vm.RemoveSelected();
        Assert.Single(vm.Files);
        Assert.Single(project.Features[0].Files);
        Assert.Equal("lib.dll", vm.Files[0].Source);
    }

    [Fact]
    public void RemoveSelected_WhenNull_DoesNothing()
    {
        var project = CreateProject();
        var vm = new FilesEditorViewModel(project);
        vm.SelectedFile = null;
        vm.RemoveSelected(); // should not throw
        Assert.Equal(2, vm.Files.Count);
    }

    [Fact]
    public void FileEntry_SourceChange_UpdatesModel()
    {
        var model = new FileEntry { Source = "old.dll" };
        var vm = new FileEntryViewModel(model, "Main");
        vm.Source = "new.dll";
        Assert.Equal("new.dll", model.Source);
    }

    [Fact]
    public void AddFile_SetsSelectedFile()
    {
        var project = CreateProject();
        var vm = new FilesEditorViewModel(project);
        vm.AddFile("selected.dll", "Main");
        Assert.NotNull(vm.SelectedFile);
        Assert.Equal("selected.dll", vm.SelectedFile!.Source);
    }
}
```

**Step 3:** Create FilesEditorView XAML

`src/FalkForge.Studio/Editors/FilesEditor/FilesEditorView.xaml`:
```xml
<UserControl x:Class="FalkForge.Studio.Editors.FilesEditor.FilesEditorView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <DockPanel Margin="8">
        <TextBlock DockPanel.Dock="Top" Text="Files" FontSize="18" FontWeight="Bold" Margin="0,0,0,8" />

        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,0,0,8">
            <Button Content="Add File..." Click="AddFile_Click" Padding="8,4" Margin="0,0,8,0" />
            <Button Content="Remove" Click="Remove_Click" Padding="8,4" />
        </StackPanel>

        <DataGrid ItemsSource="{Binding Files}" SelectedItem="{Binding SelectedFile}"
                  AutoGenerateColumns="False" CanUserAddRows="False" CanUserDeleteRows="False"
                  SelectionMode="Single">
            <DataGrid.Columns>
                <DataGridTextColumn Header="Source Path" Binding="{Binding Source}" Width="*" />
                <DataGridTextColumn Header="Target Directory" Binding="{Binding TargetDirectory}" Width="200" />
                <DataGridTextColumn Header="Feature" Binding="{Binding FeatureId}" Width="120" IsReadOnly="True" />
            </DataGrid.Columns>
        </DataGrid>
    </DockPanel>
</UserControl>
```

`src/FalkForge.Studio/Editors/FilesEditor/FilesEditorView.xaml.cs`:
```csharp
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace FalkForge.Studio.Editors.FilesEditor;

public partial class FilesEditorView : UserControl
{
    private FilesEditorViewModel ViewModel => (FilesEditorViewModel)DataContext;

    public FilesEditorView()
    {
        InitializeComponent();
    }

    private void AddFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Title = "Select files to include"
        };
        if (dialog.ShowDialog() == true)
        {
            // Use first feature as default target
            var featureId = "Main";
            foreach (var file in dialog.FileNames)
                ViewModel.AddFile(file, featureId);
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
        => ViewModel.RemoveSelected();
}
```

**Step 4:** Wire into StudioViewModel — add to NavigateTo switch:

```csharp
"files" => new FilesEditorViewModel(_project),
```

Add DataTemplate for `FilesEditorViewModel` in `StudioWindow.xaml`.

**Step 5:** Verify

Run: `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Studio.Tests/FalkForge.Studio.Tests.csproj`
Expected: 20 tests pass

**Step 6:** Commit

```
feat(studio): add files editor with add/remove
```

---

### Task 5: Features Editor

**Files:**
- Create: `src/FalkForge.Studio/Editors/FeaturesEditor/FeaturesEditorViewModel.cs`
- Create: `src/FalkForge.Studio/Editors/FeaturesEditor/FeaturesEditorView.xaml`
- Create: `src/FalkForge.Studio/Editors/FeaturesEditor/FeaturesEditorView.xaml.cs`
- Modify: `src/FalkForge.Studio/Shell/StudioViewModel.cs` — wire features editor
- Modify: `src/FalkForge.Studio/Shell/StudioWindow.xaml` — add DataTemplate
- Create: `tests/FalkForge.Studio.Tests/Editors/FeaturesEditorViewModelTests.cs`

**Context:**
The features editor shows a flat list of features (nested features in a future version). Each feature has: Id, Title, Description, IsDefault, IsRequired, and a count of files assigned. Users can add/remove features and edit their properties inline.

**Step 1:** Create FeaturesEditorViewModel

`src/FalkForge.Studio/Editors/FeaturesEditor/FeaturesEditorViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.FeaturesEditor;

public sealed class FeatureNodeViewModel : ViewModelBase
{
    private readonly FeatureSection _model;

    public FeatureNodeViewModel(FeatureSection model)
    {
        _model = model;
    }

    public FeatureSection Model => _model;

    public string Id
    {
        get => _model.Id;
        set { _model.Id = value; OnPropertyChanged(); }
    }

    public string Title
    {
        get => _model.Title;
        set { _model.Title = value; OnPropertyChanged(); }
    }

    public string? Description
    {
        get => _model.Description;
        set { _model.Description = value; OnPropertyChanged(); }
    }

    public bool IsDefault
    {
        get => _model.IsDefault;
        set { _model.IsDefault = value; OnPropertyChanged(); }
    }

    public bool IsRequired
    {
        get => _model.IsRequired;
        set { _model.IsRequired = value; OnPropertyChanged(); }
    }

    public int FileCount => _model.Files.Count;
}

public sealed class FeaturesEditorViewModel : ViewModelBase
{
    private readonly StudioProject _project;
    private FeatureNodeViewModel? _selectedFeature;

    public ObservableCollection<FeatureNodeViewModel> Features { get; } = [];

    public FeatureNodeViewModel? SelectedFeature
    {
        get => _selectedFeature;
        set => SetProperty(ref _selectedFeature, value);
    }

    public FeaturesEditorViewModel(StudioProject project)
    {
        _project = project;
        LoadFeatures();
    }

    private void LoadFeatures()
    {
        Features.Clear();
        foreach (var feature in _project.Features)
            Features.Add(new FeatureNodeViewModel(feature));
    }

    public void AddFeature(string id, string title)
    {
        var section = new FeatureSection { Id = id, Title = title, IsDefault = true };
        _project.Features.Add(section);
        var vm = new FeatureNodeViewModel(section);
        Features.Add(vm);
        SelectedFeature = vm;
    }

    public void RemoveSelected()
    {
        if (SelectedFeature is null) return;
        if (_project.Features.Count <= 1) return; // Must keep at least one feature

        _project.Features.Remove(SelectedFeature.Model);
        Features.Remove(SelectedFeature);
        SelectedFeature = Features.Count > 0 ? Features[0] : null;
    }
}
```

**Step 2:** Write tests

`tests/FalkForge.Studio.Tests/Editors/FeaturesEditorViewModelTests.cs`:
```csharp
using FalkForge.Studio.Editors.FeaturesEditor;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Tests.Editors;

public class FeaturesEditorViewModelTests
{
    [Fact]
    public void Constructor_LoadsFeatures()
    {
        var project = StudioProjectLoader.NewProject();
        var vm = new FeaturesEditorViewModel(project);
        Assert.Single(vm.Features);
        Assert.Equal("Main", vm.Features[0].Id);
    }

    [Fact]
    public void AddFeature_AddsToCollectionAndModel()
    {
        var project = StudioProjectLoader.NewProject();
        var vm = new FeaturesEditorViewModel(project);
        vm.AddFeature("Extras", "Extra Components");
        Assert.Equal(2, vm.Features.Count);
        Assert.Equal(2, project.Features.Count);
        Assert.Equal("Extras", vm.Features[1].Id);
    }

    [Fact]
    public void RemoveSelected_RemovesFeature()
    {
        var project = StudioProjectLoader.NewProject();
        project.Features.Add(new FeatureSection { Id = "Second", Title = "Second" });
        var vm = new FeaturesEditorViewModel(project);
        vm.SelectedFeature = vm.Features[1];
        vm.RemoveSelected();
        Assert.Single(vm.Features);
        Assert.Single(project.Features);
    }

    [Fact]
    public void RemoveSelected_CannotRemoveLastFeature()
    {
        var project = StudioProjectLoader.NewProject();
        var vm = new FeaturesEditorViewModel(project);
        vm.SelectedFeature = vm.Features[0];
        vm.RemoveSelected();
        Assert.Single(vm.Features); // Still there
        Assert.Single(project.Features);
    }

    [Fact]
    public void FeatureNode_PropertyChange_UpdatesModel()
    {
        var model = new FeatureSection { Id = "Test", Title = "Test" };
        var vm = new FeatureNodeViewModel(model);
        vm.Title = "Updated";
        Assert.Equal("Updated", model.Title);
    }

    [Fact]
    public void AddFeature_SetsSelected()
    {
        var project = StudioProjectLoader.NewProject();
        var vm = new FeaturesEditorViewModel(project);
        vm.AddFeature("New", "New Feature");
        Assert.Equal("New", vm.SelectedFeature?.Id);
    }
}
```

**Step 3:** Create FeaturesEditorView XAML

`src/FalkForge.Studio/Editors/FeaturesEditor/FeaturesEditorView.xaml`:
```xml
<UserControl x:Class="FalkForge.Studio.Editors.FeaturesEditor.FeaturesEditorView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <DockPanel Margin="8">
        <TextBlock DockPanel.Dock="Top" Text="Features" FontSize="18" FontWeight="Bold" Margin="0,0,0,8" />

        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,0,0,8">
            <Button Content="Add Feature" Click="AddFeature_Click" Padding="8,4" Margin="0,0,8,0" />
            <Button Content="Remove" Click="Remove_Click" Padding="8,4" />
        </StackPanel>

        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="250" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>

            <ListBox Grid.Column="0" ItemsSource="{Binding Features}"
                     SelectedItem="{Binding SelectedFeature}" Margin="0,0,8,0">
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <StackPanel>
                            <TextBlock Text="{Binding Title}" FontWeight="SemiBold" />
                            <TextBlock Text="{Binding Id, StringFormat='ID: {0}'}" FontSize="11" Foreground="Gray" />
                        </StackPanel>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>

            <StackPanel Grid.Column="1" DataContext="{Binding SelectedFeature}"
                        Visibility="{Binding DataContext.SelectedFeature, RelativeSource={RelativeSource AncestorType=UserControl}, Converter={StaticResource NullToCollapsedConverter}}">
                <TextBlock Text="Feature ID" FontWeight="SemiBold" Margin="0,0,0,4" />
                <TextBox Text="{Binding Id, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,12" />

                <TextBlock Text="Title" FontWeight="SemiBold" Margin="0,0,0,4" />
                <TextBox Text="{Binding Title, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,12" />

                <TextBlock Text="Description" FontWeight="SemiBold" Margin="0,0,0,4" />
                <TextBox Text="{Binding Description, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,12" />

                <CheckBox Content="Default (installed by default)" IsChecked="{Binding IsDefault}" Margin="0,0,0,8" />
                <CheckBox Content="Required (cannot be deselected)" IsChecked="{Binding IsRequired}" Margin="0,0,0,8" />

                <TextBlock Text="{Binding FileCount, StringFormat='{}{0} files assigned'}" Foreground="Gray" Margin="0,8,0,0" />
            </StackPanel>
        </Grid>
    </DockPanel>
</UserControl>
```

`src/FalkForge.Studio/Editors/FeaturesEditor/FeaturesEditorView.xaml.cs`:
```csharp
using System.Windows;
using System.Windows.Controls;

namespace FalkForge.Studio.Editors.FeaturesEditor;

public partial class FeaturesEditorView : UserControl
{
    private FeaturesEditorViewModel ViewModel => (FeaturesEditorViewModel)DataContext;

    public FeaturesEditorView()
    {
        InitializeComponent();
    }

    private void AddFeature_Click(object sender, RoutedEventArgs e)
    {
        var id = $"Feature{ViewModel.Features.Count + 1}";
        ViewModel.AddFeature(id, $"Feature {ViewModel.Features.Count + 1}");
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
        => ViewModel.RemoveSelected();
}
```

**Step 4:** Wire into StudioViewModel — add to NavigateTo switch:

```csharp
"features" => new FeaturesEditorViewModel(_project),
```

Add DataTemplate for `FeaturesEditorViewModel` in `StudioWindow.xaml`.

**Step 5:** Verify

Run: `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Studio.Tests/FalkForge.Studio.Tests.csproj`
Expected: 26 tests pass

**Step 6:** Commit

```
feat(studio): add features editor with add/remove
```

---

### Task 6: UI & Build Settings Editor

**Files:**
- Create: `src/FalkForge.Studio/Editors/UiEditor/UiEditorViewModel.cs`
- Create: `src/FalkForge.Studio/Editors/UiEditor/UiEditorView.xaml`
- Create: `src/FalkForge.Studio/Editors/UiEditor/UiEditorView.xaml.cs`
- Create: `src/FalkForge.Studio/Editors/BuildSettingsEditor/BuildSettingsEditorViewModel.cs`
- Create: `src/FalkForge.Studio/Editors/BuildSettingsEditor/BuildSettingsEditorView.xaml`
- Create: `src/FalkForge.Studio/Editors/BuildSettingsEditor/BuildSettingsEditorView.xaml.cs`
- Modify: `src/FalkForge.Studio/Shell/StudioViewModel.cs` — wire both editors
- Create: `tests/FalkForge.Studio.Tests/Editors/UiEditorViewModelTests.cs`
- Create: `tests/FalkForge.Studio.Tests/Editors/BuildSettingsEditorViewModelTests.cs`

**Context:**
UI editor: dropdown for dialog set (None/Minimal/InstallDir/FeatureTree/Mondo/Advanced), file picker for license RTF.
Build settings editor: output path text field, compression dropdown (None/Low/Medium/High).

These are the two simplest editors. Maps to `PackageBuilder.UseDialogSet()`, `PackageBuilder.LicenseFile`, and `MsiCompiler` output path + `PackageBuilder.Compression`.

**Step 1:** Create UiEditorViewModel

`src/FalkForge.Studio/Editors/UiEditor/UiEditorViewModel.cs`:
```csharp
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.UiEditor;

public sealed class UiEditorViewModel : ViewModelBase
{
    private readonly UiSection _model;

    public UiEditorViewModel(UiSection model)
    {
        _model = model;
    }

    public string DialogSet
    {
        get => _model.DialogSet;
        set { _model.DialogSet = value; OnPropertyChanged(); }
    }

    public string? LicenseFile
    {
        get => _model.LicenseFile;
        set { _model.LicenseFile = value; OnPropertyChanged(); }
    }

    public string[] DialogSets { get; } = ["None", "Minimal", "InstallDir", "FeatureTree", "Mondo", "Advanced"];
}
```

**Step 2:** Create BuildSettingsEditorViewModel

`src/FalkForge.Studio/Editors/BuildSettingsEditor/BuildSettingsEditorViewModel.cs`:
```csharp
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Editors.BuildSettingsEditor;

public sealed class BuildSettingsEditorViewModel : ViewModelBase
{
    private readonly BuildSection _model;

    public BuildSettingsEditorViewModel(BuildSection model)
    {
        _model = model;
    }

    public string OutputPath
    {
        get => _model.OutputPath;
        set { _model.OutputPath = value; OnPropertyChanged(); }
    }

    public string Compression
    {
        get => _model.Compression;
        set { _model.Compression = value; OnPropertyChanged(); }
    }

    public string[] CompressionLevels { get; } = ["None", "Low", "Medium", "High"];
}
```

**Step 3:** Write tests

`tests/FalkForge.Studio.Tests/Editors/UiEditorViewModelTests.cs`:
```csharp
using FalkForge.Studio.Editors.UiEditor;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Tests.Editors;

public class UiEditorViewModelTests
{
    [Fact]
    public void DialogSet_ReadsFromModel()
    {
        var model = new UiSection { DialogSet = "FeatureTree" };
        var vm = new UiEditorViewModel(model);
        Assert.Equal("FeatureTree", vm.DialogSet);
    }

    [Fact]
    public void DialogSet_Set_UpdatesModel()
    {
        var model = new UiSection();
        var vm = new UiEditorViewModel(model);
        vm.DialogSet = "Mondo";
        Assert.Equal("Mondo", model.DialogSet);
    }

    [Fact]
    public void LicenseFile_Set_UpdatesModel()
    {
        var model = new UiSection();
        var vm = new UiEditorViewModel(model);
        vm.LicenseFile = "license.rtf";
        Assert.Equal("license.rtf", model.LicenseFile);
    }

    [Fact]
    public void DialogSets_ContainsAll()
    {
        var vm = new UiEditorViewModel(new UiSection());
        Assert.Equal(6, vm.DialogSets.Length);
        Assert.Contains("FeatureTree", vm.DialogSets);
    }
}
```

`tests/FalkForge.Studio.Tests/Editors/BuildSettingsEditorViewModelTests.cs`:
```csharp
using FalkForge.Studio.Editors.BuildSettingsEditor;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Tests.Editors;

public class BuildSettingsEditorViewModelTests
{
    [Fact]
    public void OutputPath_ReadsFromModel()
    {
        var model = new BuildSection { OutputPath = "dist/" };
        var vm = new BuildSettingsEditorViewModel(model);
        Assert.Equal("dist/", vm.OutputPath);
    }

    [Fact]
    public void Compression_Set_UpdatesModel()
    {
        var model = new BuildSection();
        var vm = new BuildSettingsEditorViewModel(model);
        vm.Compression = "Low";
        Assert.Equal("Low", model.Compression);
    }

    [Fact]
    public void CompressionLevels_ContainsFourValues()
    {
        var vm = new BuildSettingsEditorViewModel(new BuildSection());
        Assert.Equal(4, vm.CompressionLevels.Length);
    }
}
```

**Step 4:** Create XAML views

`src/FalkForge.Studio/Editors/UiEditor/UiEditorView.xaml`:
```xml
<UserControl x:Class="FalkForge.Studio.Editors.UiEditor.UiEditorView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel Margin="8" MaxWidth="500">
        <TextBlock Text="UI &amp; Dialogs" FontSize="18" FontWeight="Bold" Margin="0,0,0,16" />

        <TextBlock Text="Dialog Set" FontWeight="SemiBold" Margin="0,0,0,4" />
        <ComboBox ItemsSource="{Binding DialogSets}" SelectedItem="{Binding DialogSet}" Margin="0,0,0,12" />

        <TextBlock Text="License File (RTF)" FontWeight="SemiBold" Margin="0,0,0,4" />
        <DockPanel Margin="0,0,0,12">
            <Button DockPanel.Dock="Right" Content="Browse..." Click="BrowseLicense_Click" Padding="8,4" Margin="4,0,0,0" />
            <TextBox Text="{Binding LicenseFile, UpdateSourceTrigger=PropertyChanged}" />
        </DockPanel>
    </StackPanel>
</UserControl>
```

`src/FalkForge.Studio/Editors/UiEditor/UiEditorView.xaml.cs`:
```csharp
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace FalkForge.Studio.Editors.UiEditor;

public partial class UiEditorView : UserControl
{
    private UiEditorViewModel ViewModel => (UiEditorViewModel)DataContext;

    public UiEditorView()
    {
        InitializeComponent();
    }

    private void BrowseLicense_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "RTF files (*.rtf)|*.rtf|All files (*.*)|*.*",
            Title = "Select license file"
        };
        if (dialog.ShowDialog() == true)
            ViewModel.LicenseFile = dialog.FileName;
    }
}
```

`src/FalkForge.Studio/Editors/BuildSettingsEditor/BuildSettingsEditorView.xaml`:
```xml
<UserControl x:Class="FalkForge.Studio.Editors.BuildSettingsEditor.BuildSettingsEditorView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel Margin="8" MaxWidth="500">
        <TextBlock Text="Build Settings" FontSize="18" FontWeight="Bold" Margin="0,0,0,16" />

        <TextBlock Text="Output Path" FontWeight="SemiBold" Margin="0,0,0,4" />
        <TextBox Text="{Binding OutputPath, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,12" />

        <TextBlock Text="Compression" FontWeight="SemiBold" Margin="0,0,0,4" />
        <ComboBox ItemsSource="{Binding CompressionLevels}" SelectedItem="{Binding Compression}" Margin="0,0,0,12" />
    </StackPanel>
</UserControl>
```

`src/FalkForge.Studio/Editors/BuildSettingsEditor/BuildSettingsEditorView.xaml.cs`:
```csharp
using System.Windows.Controls;

namespace FalkForge.Studio.Editors.BuildSettingsEditor;

public partial class BuildSettingsEditorView : UserControl
{
    public BuildSettingsEditorView()
    {
        InitializeComponent();
    }
}
```

**Step 5:** Wire into StudioViewModel — add to NavigateTo switch:

```csharp
"ui" => new UiEditorViewModel(_project.Ui),
"build" => new BuildSettingsEditorViewModel(_project.Build),
```

Add DataTemplates for both ViewModels in `StudioWindow.xaml`.

**Step 6:** Verify

Run: `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Studio.Tests/FalkForge.Studio.Tests.csproj`
Expected: 33 tests pass

**Step 7:** Commit

```
feat(studio): add UI and build settings editors
```

---

### Task 7: Build Service — PackageBuilder Mapping + MsiCompiler

**Files:**
- Create: `src/FalkForge.Studio/Project/StudioBuildService.cs`
- Modify: `src/FalkForge.Studio/FalkForge.Studio.csproj` — add Compiler.Msi reference
- Modify: `src/FalkForge.Studio/Shell/StudioViewModel.cs` — wire build command
- Modify: `src/FalkForge.Studio/Shell/StudioWindow.xaml.cs` — wire Build_Click
- Create: `tests/FalkForge.Studio.Tests/Project/StudioBuildServiceTests.cs`

**Context:**
This is the core of Studio — the bridge from `StudioProject` → `PackageBuilder` → `PackageModel` → `MsiCompiler.Compile()`. The `StudioBuildService` takes a `StudioProject` and a base directory, configures a `PackageBuilder`, calls `Build()`, then `MsiCompiler.Compile()`.

Reference: `JsonConfigLoader.BuildPackageModel()` at `src/FalkForge.Cli/JsonConfigLoader.cs:44-173` does the same mapping from `InstallerConfig` → `PackageBuilder`. We follow the same pattern.

Key mappings:
- `ProductSection.Architecture` → `Enum.Parse<ProcessorArchitecture>()`
- `ProductSection.Scope` → `InstallScope.PerMachine` or `InstallScope.PerUser`
- `UiSection.DialogSet` → `Enum.Parse<MsiDialogSet>()`
- `BuildSection.Compression` → `Enum.Parse<CompressionLevel>()`
- `FeatureSection` → `PackageBuilder.Feature(id, fb => fb.Files(...))`
- `FileEntry.Source` → `FileSetBuilder.Add(source)`
- `InstallDirectory` → `KnownFolder.ProgramFiles / path`

**Step 1:** Add project reference

In `FalkForge.Studio.csproj` add:
```xml
<ProjectReference Include="..\FalkForge.Compiler.Msi\FalkForge.Compiler.Msi.csproj" />
```

**Step 2:** Create StudioBuildService

`src/FalkForge.Studio/Project/StudioBuildService.cs`:
```csharp
using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

namespace FalkForge.Studio.Project;

public static class StudioBuildService
{
    public static Result<PackageModel> BuildModel(StudioProject project, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(project.Product.Name))
            return Result<PackageModel>.Failure(new Error(ErrorKind.Validation, "Product name is required."));
        if (string.IsNullOrWhiteSpace(project.Product.Manufacturer))
            return Result<PackageModel>.Failure(new Error(ErrorKind.Validation, "Manufacturer is required."));

        var builder = new PackageBuilder
        {
            Name = project.Product.Name,
            Manufacturer = project.Product.Manufacturer
        };

        // Version
        if (!string.IsNullOrWhiteSpace(project.Product.Version))
        {
            if (!Version.TryParse(project.Product.Version, out var version))
                return Result<PackageModel>.Failure(new Error(ErrorKind.Validation, $"Invalid version: {project.Product.Version}"));
            builder.Version = version;
        }

        // UpgradeCode
        if (!string.IsNullOrWhiteSpace(project.Product.UpgradeCode))
        {
            if (!Guid.TryParse(project.Product.UpgradeCode, out var upgradeCode))
                return Result<PackageModel>.Failure(new Error(ErrorKind.Validation, $"Invalid upgrade code: {project.Product.UpgradeCode}"));
            builder.UpgradeCode = upgradeCode;
        }

        // Description
        if (!string.IsNullOrWhiteSpace(project.Product.Description))
            builder.Description = project.Product.Description;

        // Architecture
        if (Enum.TryParse<ProcessorArchitecture>(project.Product.Architecture, true, out var arch))
            builder.Architecture = arch;

        // Scope
        builder.Scope = project.Product.Scope?.Equals("perUser", StringComparison.OrdinalIgnoreCase) == true
            ? InstallScope.PerUser
            : InstallScope.PerMachine;

        // Install Directory
        if (!string.IsNullOrWhiteSpace(project.InstallDirectory))
            builder.DefaultInstallDirectory = KnownFolder.ProgramFiles / project.InstallDirectory;

        // Dialog Set
        if (Enum.TryParse<MsiDialogSet>(project.Ui.DialogSet, true, out var dialogSet))
            builder.UseDialogSet(dialogSet);

        // License
        if (!string.IsNullOrWhiteSpace(project.Ui.LicenseFile))
        {
            var licensePath = Path.IsPathRooted(project.Ui.LicenseFile)
                ? project.Ui.LicenseFile
                : Path.Combine(baseDirectory, project.Ui.LicenseFile);
            builder.LicenseFile = licensePath;
        }

        // Compression
        if (Enum.TryParse<CompressionLevel>(project.Build.Compression, true, out var compression))
            builder.Compression = compression;

        // Features + Files
        foreach (var featureSection in project.Features)
        {
            if (string.IsNullOrWhiteSpace(featureSection.Id))
                return Result<PackageModel>.Failure(new Error(ErrorKind.Validation, "Feature must have an id."));

            builder.Feature(featureSection.Id, fb =>
            {
                if (featureSection.Files.Count > 0)
                {
                    fb.Files(fs =>
                    {
                        foreach (var file in featureSection.Files)
                        {
                            var sourcePath = Path.IsPathRooted(file.Source)
                                ? file.Source
                                : Path.Combine(baseDirectory, file.Source);
                            fs.Add(sourcePath);

                            if (!string.IsNullOrWhiteSpace(file.TargetDirectory))
                                fs.To(KnownFolder.ProgramFiles / file.TargetDirectory);
                        }
                    });
                }
            });
        }

        try
        {
            var model = builder.Build();
            return Result<PackageModel>.Success(model);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return Result<PackageModel>.Failure(new Error(ErrorKind.InvalidConfiguration, $"Build error: {ex.Message}"));
        }
    }

    public static Result<string> Compile(StudioProject project, string baseDirectory)
    {
        var modelResult = BuildModel(project, baseDirectory);
        if (modelResult.IsFailure)
            return Result<string>.Failure(modelResult.Error);

        var outputDir = Path.IsPathRooted(project.Build.OutputPath)
            ? project.Build.OutputPath
            : Path.Combine(baseDirectory, project.Build.OutputPath);

        Directory.CreateDirectory(outputDir);
        var outputPath = Path.Combine(outputDir, $"{project.Product.Name}.msi");

        return MsiCompiler.Compile(modelResult.Value, outputPath);
    }
}
```

**Step 3:** Write tests

`tests/FalkForge.Studio.Tests/Project/StudioBuildServiceTests.cs`:
```csharp
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Tests.Project;

public class StudioBuildServiceTests
{
    [Fact]
    public void BuildModel_MissingName_ReturnsFailure()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "";
        var result = StudioBuildService.BuildModel(project, ".");
        Assert.True(result.IsFailure);
        Assert.Contains("name", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModel_MissingManufacturer_ReturnsFailure()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Manufacturer = "";
        var result = StudioBuildService.BuildModel(project, ".");
        Assert.True(result.IsFailure);
        Assert.Contains("manufacturer", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModel_InvalidVersion_ReturnsFailure()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "Test";
        project.Product.Manufacturer = "Corp";
        project.Product.Version = "bad";
        var result = StudioBuildService.BuildModel(project, ".");
        Assert.True(result.IsFailure);
        Assert.Contains("version", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModel_InvalidUpgradeCode_ReturnsFailure()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "Test";
        project.Product.Manufacturer = "Corp";
        project.Product.UpgradeCode = "not-a-guid";
        var result = StudioBuildService.BuildModel(project, ".");
        Assert.True(result.IsFailure);
        Assert.Contains("upgrade code", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModel_ValidProject_ReturnsSuccess()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "TestApp";
        project.Product.Manufacturer = "TestCorp";
        project.Product.Version = "1.0.0";
        project.InstallDirectory = "TestCorp/TestApp";
        // No files — just metadata. Build should succeed.
        var result = StudioBuildService.BuildModel(project, ".");
        Assert.True(result.IsSuccess);
        Assert.Equal("TestApp", result.Value.Name);
        Assert.Equal("TestCorp", result.Value.Manufacturer);
    }

    [Fact]
    public void BuildModel_SetsArchitecture()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "Test";
        project.Product.Manufacturer = "Corp";
        project.Product.Architecture = "arm64";
        var result = StudioBuildService.BuildModel(project, ".");
        Assert.True(result.IsSuccess);
        Assert.Equal(FalkForge.ProcessorArchitecture.Arm64, result.Value.Architecture);
    }

    [Fact]
    public void BuildModel_SetsScope()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "Test";
        project.Product.Manufacturer = "Corp";
        project.Product.Scope = "perUser";
        var result = StudioBuildService.BuildModel(project, ".");
        Assert.True(result.IsSuccess);
        Assert.Equal(FalkForge.InstallScope.PerUser, result.Value.Scope);
    }

    [Fact]
    public void BuildModel_SetsDialogSet()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "Test";
        project.Product.Manufacturer = "Corp";
        project.Ui.DialogSet = "FeatureTree";
        var result = StudioBuildService.BuildModel(project, ".");
        Assert.True(result.IsSuccess);
        Assert.Equal(FalkForge.Models.MsiDialogSet.FeatureTree, result.Value.DialogSet);
    }

    [Fact]
    public void BuildModel_EmptyFeatureId_ReturnsFailure()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "Test";
        project.Product.Manufacturer = "Corp";
        project.Features[0].Id = "";
        var result = StudioBuildService.BuildModel(project, ".");
        Assert.True(result.IsFailure);
        Assert.Contains("Feature", result.Error.Message);
    }
}
```

**Step 4:** Wire build into StudioViewModel

Add to StudioViewModel:
```csharp
public void Build(string baseDirectory)
{
    OutputText = "Building...\n";
    var result = StudioBuildService.Compile(_project, baseDirectory);
    OutputText += result.IsSuccess
        ? $"Build succeeded: {result.Value}\n"
        : $"Build failed: {result.Error.Message}\n";
}
```

Wire `Build_Click` in `StudioWindow.xaml.cs`:
```csharp
private void Build_Click(object sender, RoutedEventArgs e)
{
    var baseDir = Environment.CurrentDirectory;
    ViewModel.Build(baseDir);
}
```

**Step 5:** Verify

Run: `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Studio.Tests/FalkForge.Studio.Tests.csproj`
Expected: 42 tests pass

**Step 6:** Commit

```
feat(studio): add build service mapping StudioProject to PackageBuilder
```

---

### Task 8: Integration Test + Full Verification

**Files:**
- Create: `tests/FalkForge.Studio.Tests/Integration/BuildIntegrationTests.cs`
- No production code changes

**Context:**
End-to-end test: create a `StudioProject`, add a real file, call `StudioBuildService.Compile()`, verify MSI file is created. This validates the full pipeline: JSON model → PackageBuilder → PackageModel → MsiCompiler → MSI.

**Step 1:** Write integration test

`tests/FalkForge.Studio.Tests/Integration/BuildIntegrationTests.cs`:
```csharp
using System.IO;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Tests.Integration;

public class BuildIntegrationTests
{
    [Fact]
    public void FullPipeline_CreatesModel_WithCorrectProperties()
    {
        var project = new StudioProject
        {
            Product = new ProductSection
            {
                Name = "IntegrationTest",
                Manufacturer = "TestCorp",
                Version = "1.2.3",
                UpgradeCode = Guid.NewGuid().ToString(),
                Architecture = "x64",
                Scope = "perMachine",
                Description = "Integration test product"
            },
            InstallDirectory = "TestCorp/IntegrationTest",
            Features =
            [
                new FeatureSection
                {
                    Id = "Main",
                    Title = "Main Application",
                    IsDefault = true,
                    IsRequired = true
                }
            ],
            Ui = new UiSection { DialogSet = "Minimal" },
            Build = new BuildSection { OutputPath = "out/", Compression = "High" }
        };

        var result = StudioBuildService.BuildModel(project, Path.GetTempPath());

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        Assert.Equal("IntegrationTest", result.Value.Name);
        Assert.Equal("TestCorp", result.Value.Manufacturer);
        Assert.Equal(new Version("1.2.3"), result.Value.Version);
        Assert.Equal(FalkForge.ProcessorArchitecture.X64, result.Value.Architecture);
        Assert.Equal(FalkForge.InstallScope.PerMachine, result.Value.Scope);
        Assert.Equal(FalkForge.Models.MsiDialogSet.Minimal, result.Value.DialogSet);
        Assert.Equal(FalkForge.CompressionLevel.High, result.Value.Compression);
    }

    [Fact]
    public void ProjectRoundTrip_ThenBuild_Succeeds()
    {
        var project = StudioProjectLoader.NewProject();
        project.Product.Name = "RoundTripApp";
        project.Product.Manufacturer = "TestCorp";

        var json = StudioProjectLoader.Serialize(project);
        var loaded = StudioProjectLoader.Deserialize(json);

        var result = StudioBuildService.BuildModel(loaded, ".");
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : "");
        Assert.Equal("RoundTripApp", result.Value.Name);
    }
}
```

**Step 2:** Run full test suite

Run: `dotnet test D:/Git/FalkInstaller/tests/FalkForge.Studio.Tests/FalkForge.Studio.Tests.csproj`
Expected: 44 tests pass

**Step 3:** Build entire solution

Run: `dotnet build D:/Git/FalkInstaller/FalkForge.slnx`
Expected: 0 errors, 0 warnings

**Step 4:** Run all solution tests

Run: `dotnet test D:/Git/FalkInstaller/FalkForge.slnx`
Expected: All tests pass (2948+ existing + 44 new)

**Step 5:** Commit

```
feat(studio): add integration tests for full build pipeline
```

---

## Critical Files Reference

| File | Role |
|------|------|
| `src/FalkForge.Core/Builders/PackageBuilder.cs` | Fluent API — 57 methods, builds `PackageModel` |
| `src/FalkForge.Core/Builders/FileSetBuilder.cs` | `FromDirectory()`, `Add()`, `To()` |
| `src/FalkForge.Core/Builders/FeatureBuilder.cs` | `Feature()`, `Files()`, `Condition()` |
| `src/FalkForge.Core/Models/PackageModel.cs` | Immutable model — 51 properties |
| `src/FalkForge.Compiler.Msi/MsiCompiler.cs` | `Compile(PackageModel, string) → Result<string>` |
| `src/FalkForge.Cli/JsonConfigLoader.cs:44-173` | Reference pattern for JSON → PackageBuilder mapping |
| `src/FalkForge.Core/KnownFolder.cs` | `ProgramFiles`, `AppData`, etc. + `/` operator for paths |
| `src/FalkForge.Core/InstallPath.cs` | `Root` + `RelativePath`, used by `FileSetBuilder.To()` |

## Enums

| Enum | Values |
|------|--------|
| `ProcessorArchitecture` | X86, X64, Arm64 |
| `InstallScope` | PerMachine, PerUser |
| `CompressionLevel` | None, Low, Medium, High |
| `MsiDialogSet` | None, Minimal, InstallDir, FeatureTree, Mondo, Advanced |
