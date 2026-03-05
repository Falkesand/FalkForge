# MAS (MultiAccess Setup) Installer Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a custom WPF bundle installer demo at `demo/MAS/` that replicates the classic MultiAccess installer UI with 6 pages (Welcome, License, Installation Type, Database Server, Confirm Parameters, Advanced Stub) and working Next/Previous navigation.

**Architecture:** Custom `Window` subclass replaces the default shell. It provides a gray-gradient banner area with icon/title/subtitle, a content region for page views, and a bottom button bar with version string. Pages derive from a shared `MasPageBase<TView>` that adds `Subtitle`, `NextButtonText`, `ShowPrintButton`, `ShowPreviousButton` properties. WPF runtime reflection binding resolves these from `{Binding CurrentPage.PropertyName}` in the shell.

**Tech Stack:** .NET 10, C# latest, WPF, FalkForge.Ui (InstallerApp, InstallerPage<TView>, PageResult, InstallerState, CustomShellViewModel)

---

## Reference: Key API Surface

- `InstallerApp.Run(args, app => app.Window(w => w.CustomWindow<T>()).Pages(p => p.Add<TPage>()))` — entry point
- `CustomWindow<T>()` bypasses `ApplyConfig()`. Window gets `DataContext = CustomShellViewModel`
- `CustomShellViewModel` exposes: `CurrentPage`, `CurrentView`, `NextCommand`, `BackCommand`, `CancelCommand`, `CanGoNext`, `CanGoBack`, `StatusMessage`, `IsApplying`
- `InstallerPage<TView>` — page IS the ViewModel. `TView.DataContext = this`. Override `Title`, `CanGoNext`, `CanGoBack`, `OnNext()`, `OnBack()`, `OnNavigatedToAsync()`
- `PageResult.Next`, `PageResult.Previous`, `PageResult.Install`, `PageResult.GoTo<TPage>()`, `PageResult.Stay(msg)`
- `SharedState.Set<T>(key, value)`, `SharedState.Get<T>(key)` — cross-page data

---

### Task 1: Project Scaffold

**Files:**
- Create: `demo/MAS/MAS.csproj`
- Create: `demo/MAS/Program.cs`

**Step 1: Create project file**

Create `demo/MAS/MAS.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>MAS</RootNamespace>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/FalkForge.Ui/FalkForge.Ui.csproj" />
  </ItemGroup>
</Project>
```

**Step 2: Create minimal Program.cs**

Create `demo/MAS/Program.cs` with a single stub page to verify the project compiles:
```csharp
using FalkForge.Ui;
using MAS.Pages;

return InstallerApp.Run(args, app => app
    .Window(w => w
        .Size(800, 550)
        .Title("Installer for MultiAccess 8.9.0"))
    .Pages(p => p
        .Add<WelcomePage>()));
```

**Step 3: Create stub WelcomePage + WelcomeView**

Create `demo/MAS/Pages/WelcomePage.cs`:
```csharp
using FalkForge.Ui;
using MAS.Views;

namespace MAS.Pages;

public sealed class WelcomePage : InstallerPage<WelcomeView>
{
    public override string Title => "Welcome to MultiAccess setup";
    public override bool CanGoBack => false;
}
```

Create `demo/MAS/Views/WelcomeView.xaml`:
```xml
<UserControl x:Class="MAS.Views.WelcomeView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <TextBlock Text="Stub - Welcome" Margin="20" />
</UserControl>
```

Create `demo/MAS/Views/WelcomeView.xaml.cs`:
```csharp
using System.Windows.Controls;

namespace MAS.Views;

public partial class WelcomeView : UserControl
{
    public WelcomeView() => InitializeComponent();
}
```

**Step 4: Build and verify**

Run: `dotnet build demo/MAS/MAS.csproj`
Expected: Build succeeded. 0 Warning(s). 0 Error(s).

**Step 5: Commit**

```bash
git add demo/MAS/
git commit -m "scaffold MAS installer demo project"
```

---

### Task 2: MasPageBase + Theme + Custom Window Shell

**Files:**
- Create: `demo/MAS/Pages/MasPageBase.cs`
- Create: `demo/MAS/Themes/MasTheme.xaml`
- Create: `demo/MAS/Shell/MasInstallerWindow.xaml`
- Create: `demo/MAS/Shell/MasInstallerWindow.xaml.cs`
- Modify: `demo/MAS/Program.cs`

**Step 1: Create MasPageBase**

Create `demo/MAS/Pages/MasPageBase.cs`:
```csharp
using System.Windows;
using FalkForge.Ui;

namespace MAS.Pages;

public abstract class MasPageBase<TView> : InstallerPage<TView>
    where TView : FrameworkElement, new()
{
    public virtual string? Subtitle => null;
    public virtual string NextButtonText => "Next";
    public virtual bool ShowPrintButton => false;
    public virtual bool ShowPreviousButton => true;
}
```

**Step 2: Create MasTheme.xaml**

Create `demo/MAS/Themes/MasTheme.xaml`:
```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Banner -->
    <LinearGradientBrush x:Key="BannerBackground" StartPoint="0,0" EndPoint="0,1">
        <GradientStop Color="#E8E8E8" Offset="0"/>
        <GradientStop Color="#D0D0D0" Offset="1"/>
    </LinearGradientBrush>
    <SolidColorBrush x:Key="BannerSeparator" Color="#A0A0A0"/>
    <SolidColorBrush x:Key="IconBrush" Color="#CC3300"/>

    <!-- Content -->
    <SolidColorBrush x:Key="ContentBackground" Color="#F0F0F0"/>

    <!-- Button bar -->
    <SolidColorBrush x:Key="ButtonBarBackground" Color="#F0F0F0"/>
    <SolidColorBrush x:Key="ButtonBarSeparator" Color="#D0D0D0"/>
    <SolidColorBrush x:Key="VersionForeground" Color="#808080"/>

    <!-- Standard button style matching classic Windows installer -->
    <Style x:Key="InstallerButton" TargetType="Button">
        <Setter Property="MinWidth" Value="75"/>
        <Setter Property="Height" Value="23"/>
        <Setter Property="Margin" Value="4,0,0,0"/>
        <Setter Property="Padding" Value="8,1"/>
    </Style>
</ResourceDictionary>
```

**Step 3: Create MasInstallerWindow.xaml**

Create `demo/MAS/Shell/MasInstallerWindow.xaml`:
```xml
<Window x:Class="MAS.Shell.MasInstallerWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Installer for MultiAccess 8.9.0"
        Width="800" Height="550"
        WindowStartupLocation="CenterScreen"
        ResizeMode="CanMinimize"
        Background="#F0F0F0">
    <Window.Resources>
        <ResourceDictionary Source="/Themes/MasTheme.xaml"/>
    </Window.Resources>
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>   <!-- Banner -->
            <RowDefinition Height="Auto"/>   <!-- Separator -->
            <RowDefinition Height="*"/>      <!-- Content -->
            <RowDefinition Height="Auto"/>   <!-- Button bar separator -->
            <RowDefinition Height="Auto"/>   <!-- Button bar -->
        </Grid.RowDefinitions>

        <!-- Banner area -->
        <Border Grid.Row="0" Background="{StaticResource BannerBackground}" Padding="12,8">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>
                <!-- Placeholder icon (orange ellipse) -->
                <Ellipse Grid.Column="0" Width="36" Height="36"
                         Fill="{StaticResource IconBrush}" Margin="0,0,10,0"
                         VerticalAlignment="Center"/>
                <!-- Title + Subtitle -->
                <StackPanel Grid.Column="1" VerticalAlignment="Center">
                    <TextBlock Text="{Binding CurrentPage.Title}"
                               FontSize="14" FontWeight="SemiBold"/>
                    <TextBlock Text="{Binding CurrentPage.Subtitle}"
                               FontSize="11" Foreground="#555555"
                               Visibility="{Binding CurrentPage.Subtitle,
                                   Converter={StaticResource NullToCollapsedConverter},
                                   FallbackValue=Collapsed}"/>
                </StackPanel>
            </Grid>
        </Border>

        <!-- Separator line -->
        <Rectangle Grid.Row="1" Height="1"
                   Fill="{StaticResource BannerSeparator}"/>

        <!-- Page content -->
        <ContentPresenter Grid.Row="2" Content="{Binding CurrentView}"/>

        <!-- Button bar separator -->
        <Rectangle Grid.Row="3" Height="1"
                   Fill="{StaticResource ButtonBarSeparator}"/>

        <!-- Button bar -->
        <Grid Grid.Row="4" Background="{StaticResource ButtonBarBackground}"
              Margin="12,8">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>  <!-- Version string -->
                <ColumnDefinition Width="Auto"/> <!-- Buttons -->
            </Grid.ColumnDefinitions>

            <!-- Version string -->
            <TextBlock Grid.Column="0" Text="8.9.0-alpha4-000+g284fb5e-aptussw"
                       Foreground="{StaticResource VersionForeground}"
                       VerticalAlignment="Center" FontSize="10"/>

            <!-- Status message (validation errors) -->
            <TextBlock Grid.Column="0" Text="{Binding StatusMessage}"
                       Foreground="#CC0000" VerticalAlignment="Center"
                       HorizontalAlignment="Center" FontSize="11"
                       Margin="200,0,0,0"/>

            <!-- Buttons -->
            <StackPanel Grid.Column="1" Orientation="Horizontal">
                <!-- Print button (only on license page) -->
                <Button Content="Print"
                        Style="{StaticResource InstallerButton}"
                        Visibility="{Binding CurrentPage.ShowPrintButton,
                            Converter={StaticResource BoolToVisibilityConverter},
                            FallbackValue=Collapsed}"/>

                <!-- Previous button -->
                <Button Content="Previous"
                        Command="{Binding BackCommand}"
                        IsEnabled="{Binding CanGoBack}"
                        Style="{StaticResource InstallerButton}"
                        Visibility="{Binding CurrentPage.ShowPreviousButton,
                            Converter={StaticResource BoolToVisibilityConverter},
                            FallbackValue=Collapsed}"/>

                <!-- Next/Install button -->
                <Button Content="{Binding CurrentPage.NextButtonText,
                            FallbackValue=Next}"
                        Command="{Binding NextCommand}"
                        IsEnabled="{Binding CanGoNext}"
                        Style="{StaticResource InstallerButton}"/>

                <!-- Cancel button -->
                <Button Content="Cancel"
                        Command="{Binding CancelCommand}"
                        Style="{StaticResource InstallerButton}"/>
            </StackPanel>
        </Grid>
    </Grid>
</Window>
```

**Step 4: Create MasInstallerWindow.xaml.cs with converters**

Create `demo/MAS/Shell/MasInstallerWindow.xaml.cs`:
```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MAS.Shell;

public partial class MasInstallerWindow : Window
{
    public MasInstallerWindow()
    {
        Resources.Add("NullToCollapsedConverter", new NullToCollapsedConverter());
        Resources.Add("BoolToVisibilityConverter", new BoolToVisibilityConverter());
        InitializeComponent();
    }
}

internal sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && !string.IsNullOrEmpty(s) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

internal sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

**Step 5: Update Program.cs to use custom window**

Update `demo/MAS/Program.cs`:
```csharp
using FalkForge.Ui;
using MAS.Pages;
using MAS.Shell;

return InstallerApp.Run(args, app => app
    .Window(w => w.CustomWindow<MasInstallerWindow>())
    .Pages(p => p
        .Add<WelcomePage>()));
```

**Step 6: Update WelcomePage to derive from MasPageBase**

Update `demo/MAS/Pages/WelcomePage.cs`:
```csharp
using MAS.Views;

namespace MAS.Pages;

public sealed class WelcomePage : MasPageBase<WelcomeView>
{
    public override string Title => "Welcome to MultiAccess setup";
    public override bool CanGoBack => false;
    public override bool ShowPreviousButton => false;
}
```

**Step 7: Build and verify**

Run: `dotnet build demo/MAS/MAS.csproj`
Expected: Build succeeded. 0 Warning(s). 0 Error(s).

**Step 8: Commit**

```bash
git add demo/MAS/
git commit -m "add MAS custom window shell with banner, theme, and MasPageBase"
```

---

### Task 3: Welcome Page (Full UI)

**Files:**
- Modify: `demo/MAS/Views/WelcomeView.xaml`

**Step 1: Implement WelcomeView.xaml**

Replace stub content in `demo/MAS/Views/WelcomeView.xaml`:
```xml
<UserControl x:Class="MAS.Views.WelcomeView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="#F0F0F0">
    <Grid Margin="40,60,40,20">
        <TextBlock TextWrapping="Wrap" FontSize="13" VerticalAlignment="Center">
            <Run Text="The installation wizard installs MultiAccess (8.9.0) on your computer. Click Next to continue or Cancel to leave the installation wizard."/>
        </TextBlock>
    </Grid>
</UserControl>
```

**Step 2: Build and verify**

Run: `dotnet build demo/MAS/MAS.csproj`
Expected: Build succeeded.

**Step 3: Commit**

```bash
git add demo/MAS/Views/WelcomeView.xaml
git commit -m "implement Welcome page UI for MAS installer"
```

---

### Task 4: License Agreement Page

**Files:**
- Create: `demo/MAS/Pages/LicensePage.cs`
- Create: `demo/MAS/Views/LicenseView.xaml`
- Create: `demo/MAS/Views/LicenseView.xaml.cs`
- Modify: `demo/MAS/Program.cs`

**Step 1: Create LicensePage**

Create `demo/MAS/Pages/LicensePage.cs`:
```csharp
using MAS.Views;

namespace MAS.Pages;

public sealed class LicensePage : MasPageBase<LicenseView>
{
    private bool _accepted;

    public override string Title => "End-User License Agreement";
    public override string? Subtitle => "Please read the following license agreement carefully";
    public override bool ShowPrintButton => true;
    public override bool CanGoNext => _accepted;

    public bool Accepted
    {
        get => _accepted;
        set { SetField(ref _accepted, value); OnPropertyChanged(nameof(CanGoNext)); }
    }

    public string LicenseText => """
        LICENSE AGREEMENT

        © Copyright ASSA ABLOY OPENING SOLUTIONS SWEDEN AB. All rights reserved.

        This is a legal agreement between you, the end user and ASSA ABLOY OPENING SOLUTIONS SWEDEN AB. If you do not agree to the terms of this agreement, please cancel the installation.

        1. GRANT OF LICENCE
        ASSA ABLOY OPENING SOLUTIONS SWEDEN AB permits you to use copies of the software on single computers or on a single hard disk for use by you.

        2. COPYRIGHT
        The SOFTWARE is owned by ASSA ABLOY OPENING SOLUTIONS SWEDEN AB and is protected by Swedish copyright laws, international treaty provisions and other applicable laws.

        3. OTHER RESTRICTIONS
        You may not reverse engineer, decompile or disassemble the software except as permitted under mandatory laws. You may not amend or make changes to the software.
        """;
}
```

**Step 2: Create LicenseView.xaml**

Create `demo/MAS/Views/LicenseView.xaml`:
```xml
<UserControl x:Class="MAS.Views.LicenseView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="#F0F0F0">
    <Grid Margin="16">
        <Grid.RowDefinitions>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- License text area -->
        <TextBox Grid.Row="0"
                 Text="{Binding LicenseText, Mode=OneTime}"
                 IsReadOnly="True"
                 TextWrapping="Wrap"
                 VerticalScrollBarVisibility="Auto"
                 Background="White"
                 BorderBrush="#AAAAAA"
                 Padding="8"
                 FontSize="12"/>

        <!-- Accept checkbox -->
        <CheckBox Grid.Row="1"
                  Content="I accept the terms in the License Agreement"
                  IsChecked="{Binding Accepted}"
                  Margin="0,12,0,0"
                  FontSize="12"/>
    </Grid>
</UserControl>
```

Create `demo/MAS/Views/LicenseView.xaml.cs`:
```csharp
using System.Windows.Controls;

namespace MAS.Views;

public partial class LicenseView : UserControl
{
    public LicenseView() => InitializeComponent();
}
```

**Step 3: Register page in Program.cs**

Update `demo/MAS/Program.cs`:
```csharp
using FalkForge.Ui;
using MAS.Pages;
using MAS.Shell;

return InstallerApp.Run(args, app => app
    .Window(w => w.CustomWindow<MasInstallerWindow>())
    .Pages(p => p
        .Add<WelcomePage>()
        .Add<LicensePage>()));
```

**Step 4: Build and verify**

Run: `dotnet build demo/MAS/MAS.csproj`
Expected: Build succeeded.

**Step 5: Commit**

```bash
git add demo/MAS/
git commit -m "implement License Agreement page with EULA text and accept checkbox"
```

---

### Task 5: Installation Type Page

**Files:**
- Create: `demo/MAS/Pages/InstallationTypePage.cs`
- Create: `demo/MAS/Views/InstallationTypeView.xaml`
- Create: `demo/MAS/Views/InstallationTypeView.xaml.cs`
- Modify: `demo/MAS/Program.cs`

**Step 1: Create InstallationTypePage**

Create `demo/MAS/Pages/InstallationTypePage.cs`:
```csharp
using FalkForge.Ui.Abstractions;
using MAS.Views;

namespace MAS.Pages;

public sealed class InstallationTypePage : MasPageBase<InstallationTypeView>
{
    private bool _isStandard = true;

    public override string Title => "Select installation type";

    public bool IsStandard
    {
        get => _isStandard;
        set
        {
            if (SetField(ref _isStandard, value))
                OnPropertyChanged(nameof(IsAdvanced));
        }
    }

    public bool IsAdvanced
    {
        get => !_isStandard;
        set => IsStandard = !value;
    }

    public override PageResult OnNext()
    {
        SharedState.Set("InstallationType", _isStandard ? "Standard" : "Advanced");
        return _isStandard
            ? PageResult.GoTo<DatabaseServerPage>()
            : PageResult.GoTo<AdvancedStubPage>();
    }
}
```

**Step 2: Create InstallationTypeView.xaml**

Create `demo/MAS/Views/InstallationTypeView.xaml`:
```xml
<UserControl x:Class="MAS.Views.InstallationTypeView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="#F0F0F0">
    <Grid Margin="16,12">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="16"/>
            <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>

        <!-- Standard installation -->
        <StackPanel Grid.Column="0">
            <RadioButton Content="Standard installation"
                         IsChecked="{Binding IsStandard}"
                         FontWeight="Bold" FontSize="13" Margin="0,0,0,4"/>
            <TextBlock Text="Select this to install a standard installation"
                       TextWrapping="Wrap" Margin="20,0,0,8" FontSize="12"
                       Foreground="#333333"/>
            <Border BorderBrush="#AAAAAA" BorderThickness="1"
                    Background="White" Padding="10" Margin="4,4,4,0">
                <TextBlock TextWrapping="Wrap" FontSize="12">
                    <Run FontWeight="Bold" Text="The following will be installed:"/>
                    <LineBreak/>
                    <Run Text="1. MultiAccess"/>
                    <LineBreak/>
                    <Run Text="   * Sql server = .\SQLEXPRESS (asks if you want to"/>
                    <LineBreak/>
                    <Run Text="     install if it is missing)"/>
                    <LineBreak/>
                    <Run Text="   * Database = MultiAccess (Adds the database if it is"/>
                    <LineBreak/>
                    <Run Text="     missing)"/>
                    <LineBreak/>
                    <Run Text="2. MultiServer is started by MultiAccess and connects"/>
                    <LineBreak/>
                    <Run Text="   to the same Sql server and database as MultiAccess"/>
                    <LineBreak/>
                    <Run Text="3. Konfigurera"/>
                    <LineBreak/>
                    <Run Text="4. Concatenate"/>
                </TextBlock>
            </Border>
        </StackPanel>

        <!-- Advanced installation -->
        <StackPanel Grid.Column="2">
            <RadioButton Content="Advanced installation"
                         IsChecked="{Binding IsAdvanced}"
                         FontWeight="Bold" FontSize="13" Margin="0,0,0,4"/>
            <TextBlock TextWrapping="Wrap" Margin="20,0,0,8" FontSize="12"
                       Foreground="#333333">
                <Run Text="Choose this to be able to freely customize your installation. For example, if you need to do any of the following, an advanced installation should be selected."/>
            </TextBlock>
            <Border BorderBrush="#AAAAAA" BorderThickness="1"
                    Background="White" Padding="10" Margin="4,4,4,0">
                <TextBlock TextWrapping="Wrap" FontSize="12">
                    <Run Text="1. Install MultiAccess only"/>
                    <LineBreak/>
                    <Run Text="2. Install MultiServer only"/>
                    <LineBreak/>
                    <Run Text="3. Install MultiServer as a service"/>
                    <LineBreak/>
                    <Run Text="4. Connect to existing SQL server, local or accessible"/>
                    <LineBreak/>
                    <Run Text="   via the network"/>
                    <LineBreak/>
                    <Run Text="5. Freely choose installation directory"/>
                    <LineBreak/>
                    <Run Text="6. Choose whether the software should be installed"/>
                    <LineBreak/>
                    <Run Text="   with Integrated security or not"/>
                </TextBlock>
            </Border>
        </StackPanel>
    </Grid>
</UserControl>
```

Create `demo/MAS/Views/InstallationTypeView.xaml.cs`:
```csharp
using System.Windows.Controls;

namespace MAS.Views;

public partial class InstallationTypeView : UserControl
{
    public InstallationTypeView() => InitializeComponent();
}
```

**Step 3: Register page in Program.cs**

Update `demo/MAS/Program.cs`:
```csharp
using FalkForge.Ui;
using MAS.Pages;
using MAS.Shell;

return InstallerApp.Run(args, app => app
    .Window(w => w.CustomWindow<MasInstallerWindow>())
    .Pages(p => p
        .Add<WelcomePage>()
        .Add<LicensePage>()
        .Add<InstallationTypePage>()));
```

**Step 4: Build and verify**

Run: `dotnet build demo/MAS/MAS.csproj`
Expected: Build succeeded.

**Step 5: Commit**

```bash
git add demo/MAS/
git commit -m "implement Installation Type page with Standard/Advanced selection"
```

---

### Task 6: Database Server Page (Standard Flow)

**Files:**
- Create: `demo/MAS/Pages/DatabaseServerPage.cs`
- Create: `demo/MAS/Views/DatabaseServerView.xaml`
- Create: `demo/MAS/Views/DatabaseServerView.xaml.cs`
- Modify: `demo/MAS/Program.cs`

**Step 1: Create DatabaseServerPage**

Create `demo/MAS/Pages/DatabaseServerPage.cs`:
```csharp
using MAS.Views;

namespace MAS.Pages;

public sealed class DatabaseServerPage : MasPageBase<DatabaseServerView>
{
    private bool _useExisting = true;
    private string _databaseServer = @".\SQLEXPRESS";
    private string _databaseName = "MultiAccess";

    public override string Title => "Choose database server";
    public override string? Subtitle => "Select database for MultiAccess";

    public bool UseExisting
    {
        get => _useExisting;
        set
        {
            if (SetField(ref _useExisting, value))
                OnPropertyChanged(nameof(CreateEmpty));
        }
    }

    public bool CreateEmpty
    {
        get => !_useExisting;
        set => UseExisting = !value;
    }

    public string DatabaseServer
    {
        get => _databaseServer;
        set => SetField(ref _databaseServer, value);
    }

    public string DatabaseName
    {
        get => _databaseName;
        set => SetField(ref _databaseName, value);
    }

    public override PageResult OnNext()
    {
        SharedState.Set("UseExistingDatabase", _useExisting);
        SharedState.Set("DatabaseServer", _databaseServer);
        SharedState.Set("DatabaseName", _databaseName);
        return PageResult.Next;
    }
}
```

**Step 2: Create DatabaseServerView.xaml**

Create `demo/MAS/Views/DatabaseServerView.xaml`:
```xml
<UserControl x:Class="MAS.Views.DatabaseServerView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="#F0F0F0">
    <Grid Margin="16,12">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="16"/>
            <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>

        <!-- Left side: radio buttons + help text -->
        <StackPanel Grid.Column="0">
            <RadioButton Content="Use existing database"
                         IsChecked="{Binding UseExisting}"
                         FontWeight="SemiBold" FontSize="12"
                         Margin="0,0,0,4"/>
            <RadioButton Content="Create empty database"
                         IsChecked="{Binding CreateEmpty}"
                         FontWeight="SemiBold" FontSize="12"
                         Margin="0,0,0,12"/>
            <Border BorderBrush="#AAAAAA" BorderThickness="1"
                    Background="White" Padding="10" Margin="4,4,4,0">
                <TextBlock TextWrapping="Wrap" FontSize="12">
                    <Run Text="Specify which database server the database is located at and what the database is called"/>
                </TextBlock>
            </Border>
        </StackPanel>

        <!-- Right side: server and database fields -->
        <StackPanel Grid.Column="2">
            <!-- Database Server -->
            <TextBlock Text="Database Server:" FontWeight="SemiBold"
                       FontSize="12" Margin="0,0,0,4"/>
            <Border BorderBrush="#AAAAAA" BorderThickness="1"
                    Background="White" Padding="8" Margin="0,0,0,4">
                <StackPanel>
                    <Button Content="Search for server" HorizontalAlignment="Center"
                            Padding="16,6" Margin="0,4,0,8"/>
                    <TextBox Text="{Binding DatabaseServer, UpdateSourceTrigger=PropertyChanged}"
                             FontSize="12" Padding="4,2"/>
                </StackPanel>
            </Border>

            <!-- Database Name -->
            <TextBlock Text="Database Name" FontWeight="SemiBold"
                       FontSize="12" Margin="0,16,0,4"/>
            <TextBox Text="{Binding DatabaseName, UpdateSourceTrigger=PropertyChanged}"
                     FontSize="12" Padding="4,2"
                     BorderBrush="#AAAAAA"/>
        </StackPanel>
    </Grid>
</UserControl>
```

Create `demo/MAS/Views/DatabaseServerView.xaml.cs`:
```csharp
using System.Windows.Controls;

namespace MAS.Views;

public partial class DatabaseServerView : UserControl
{
    public DatabaseServerView() => InitializeComponent();
}
```

**Step 3: Register page in Program.cs**

Update `demo/MAS/Program.cs`:
```csharp
using FalkForge.Ui;
using MAS.Pages;
using MAS.Shell;

return InstallerApp.Run(args, app => app
    .Window(w => w.CustomWindow<MasInstallerWindow>())
    .Pages(p => p
        .Add<WelcomePage>()
        .Add<LicensePage>()
        .Add<InstallationTypePage>()
        .Add<DatabaseServerPage>()));
```

**Step 4: Build and verify**

Run: `dotnet build demo/MAS/MAS.csproj`
Expected: Build succeeded.

**Step 5: Commit**

```bash
git add demo/MAS/
git commit -m "implement Database Server page with server/database configuration"
```

---

### Task 7: Confirm Parameters Page (Standard Flow)

**Files:**
- Create: `demo/MAS/Pages/ConfirmParametersPage.cs`
- Create: `demo/MAS/Models/ParameterGroup.cs`
- Create: `demo/MAS/Views/ConfirmParametersView.xaml`
- Create: `demo/MAS/Views/ConfirmParametersView.xaml.cs`
- Modify: `demo/MAS/Program.cs`

**Step 1: Create ParameterGroup model**

Create `demo/MAS/Models/ParameterGroup.cs`:
```csharp
namespace MAS.Models;

public sealed record ParameterEntry(string Name, string Value);

public sealed class ParameterGroup
{
    public required string Header { get; init; }
    public required IReadOnlyList<ParameterEntry> Entries { get; init; }
}
```

**Step 2: Create ConfirmParametersPage**

Create `demo/MAS/Pages/ConfirmParametersPage.cs`:
```csharp
using System.Collections.ObjectModel;
using FalkForge.Ui.Abstractions;
using MAS.Models;
using MAS.Views;

namespace MAS.Pages;

public sealed class ConfirmParametersPage : MasPageBase<ConfirmParametersView>
{
    public override string Title => "Confirm Parameters";
    public override string NextButtonText => "Install";
    public override bool ShowPreviousButton => true;

    public ObservableCollection<ParameterGroup> ParameterGroups { get; } = [];

    public override Task OnNavigatedToAsync()
    {
        ParameterGroups.Clear();

        var installType = SharedState.Get<string>("InstallationType") ?? "Standard";

        ParameterGroups.Add(new ParameterGroup
        {
            Header = "Select installation type",
            Entries =
            [
                new("Concatenate", "Install"),
                new("Konfigurera", "Install"),
                new("MultiAccess", "Install"),
                new("MultiServer", "Install"),
                new("MultiServerEx", "Install"),
            ]
        });

        ParameterGroups.Add(new ParameterGroup
        {
            Header = "Installation folder for MultiAccess",
            Entries =
            [
                new("Install folder", @"C:\Program Files (x86)\Aptus\MultiAccess"),
            ]
        });

        var useExisting = SharedState.Get<bool>("UseExistingDatabase");
        var dbServer = SharedState.Get<string>("DatabaseServer") ?? @".\SQLEXPRESS";
        var dbName = SharedState.Get<string>("DatabaseName") ?? "MultiAccess";

        ParameterGroups.Add(new ParameterGroup
        {
            Header = "Choose database server",
            Entries =
            [
                new("Create database connection", "Yes"),
                new("Create empty database", useExisting ? "No" : "Yes"),
                new("Database Name", dbName),
                new("Server path", dbServer),
                new("Use existing database", useExisting ? "Yes" : "No"),
            ]
        });

        ParameterGroups.Add(new ParameterGroup
        {
            Header = "Installation folder for MultiServer",
            Entries =
            [
                new("Install folder", @"C:\Program Files (x86)\Aptus\MultiServer"),
            ]
        });

        ParameterGroups.Add(new ParameterGroup
        {
            Header = "Database Connection Settings",
            Entries =
            [
                new("Name of database:", dbName),
                new("Database server:", dbServer),
                new("Integrated security:", "Yes"),
                new("User name:", ""),
                new("Password:", ""),
            ]
        });

        ParameterGroups.Add(new ParameterGroup
        {
            Header = "MultiServer Advanced Settings",
            Entries =
            [
                new("DSN Name", dbName),
                new("Install as service", "No"),
            ]
        });

        ParameterGroups.Add(new ParameterGroup
        {
            Header = "Installation folder for MultiServerEx",
            Entries =
            [
                new("Install folder", @"C:\Program Files (x86)\Aptus\MultiServerEx"),
            ]
        });

        ParameterGroups.Add(new ParameterGroup
        {
            Header = "MultiServerEx Advanced Settings",
            Entries =
            [
                new("Install as service", "No"),
            ]
        });

        return Task.CompletedTask;
    }

    public override PageResult OnNext()
    {
        return PageResult.Install;
    }
}
```

**Step 3: Create ConfirmParametersView.xaml**

Create `demo/MAS/Views/ConfirmParametersView.xaml`:
```xml
<UserControl x:Class="MAS.Views.ConfirmParametersView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="#F0F0F0">
    <Grid Margin="16,12">
        <!-- Two-column header -->
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
            </Grid.RowDefinitions>

            <!-- Column headers -->
            <Grid Grid.Row="0" Background="#E8E8E8">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="250"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>
                <TextBlock Grid.Column="0" Text="Name" FontWeight="Bold"
                           Padding="8,4" FontSize="12"/>
                <TextBlock Grid.Column="1" Text="Value" FontWeight="Bold"
                           Padding="8,4" FontSize="12"/>
            </Grid>

            <!-- Scrollable parameter list -->
            <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto"
                          BorderBrush="#AAAAAA" BorderThickness="1"
                          Background="White">
                <ItemsControl ItemsSource="{Binding ParameterGroups}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <StackPanel>
                                <!-- Group header -->
                                <TextBlock Text="{Binding Header}"
                                           FontWeight="Bold" Foreground="#333399"
                                           Padding="8,6,8,2" FontSize="12"/>
                                <!-- Entries -->
                                <ItemsControl ItemsSource="{Binding Entries}">
                                    <ItemsControl.ItemTemplate>
                                        <DataTemplate>
                                            <Grid>
                                                <Grid.ColumnDefinitions>
                                                    <ColumnDefinition Width="250"/>
                                                    <ColumnDefinition Width="*"/>
                                                </Grid.ColumnDefinitions>
                                                <TextBlock Grid.Column="0"
                                                           Text="{Binding Name}"
                                                           Padding="24,2,8,2"
                                                           FontSize="12"/>
                                                <TextBlock Grid.Column="1"
                                                           Text="{Binding Value}"
                                                           Padding="8,2"
                                                           FontSize="12"/>
                                            </Grid>
                                        </DataTemplate>
                                    </ItemsControl.ItemTemplate>
                                </ItemsControl>
                            </StackPanel>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </ScrollViewer>
        </Grid>
    </Grid>
</UserControl>
```

Create `demo/MAS/Views/ConfirmParametersView.xaml.cs`:
```csharp
using System.Windows.Controls;

namespace MAS.Views;

public partial class ConfirmParametersView : UserControl
{
    public ConfirmParametersView() => InitializeComponent();
}
```

**Step 4: Register page in Program.cs**

Update `demo/MAS/Program.cs`:
```csharp
using FalkForge.Ui;
using MAS.Pages;
using MAS.Shell;

return InstallerApp.Run(args, app => app
    .Window(w => w.CustomWindow<MasInstallerWindow>())
    .Pages(p => p
        .Add<WelcomePage>()
        .Add<LicensePage>()
        .Add<InstallationTypePage>()
        .Add<DatabaseServerPage>()
        .Add<ConfirmParametersPage>()));
```

**Step 5: Build and verify**

Run: `dotnet build demo/MAS/MAS.csproj`
Expected: Build succeeded.

**Step 6: Commit**

```bash
git add demo/MAS/
git commit -m "implement Confirm Parameters page with grouped Name/Value grid"
```

---

### Task 8: Advanced Stub Page + Final Wiring

**Files:**
- Create: `demo/MAS/Pages/AdvancedStubPage.cs`
- Create: `demo/MAS/Views/AdvancedStubView.xaml`
- Create: `demo/MAS/Views/AdvancedStubView.xaml.cs`
- Modify: `demo/MAS/Program.cs`

**Step 1: Create AdvancedStubPage**

Create `demo/MAS/Pages/AdvancedStubPage.cs`:
```csharp
using FalkForge.Ui.Abstractions;
using MAS.Views;

namespace MAS.Pages;

public sealed class AdvancedStubPage : MasPageBase<AdvancedStubView>
{
    public override string Title => "Advanced Installation";
    public override string? Subtitle => "Advanced options will be available in a future update";

    public override PageResult OnNext()
    {
        SharedState.Set("InstallationType", "Advanced");
        return PageResult.GoTo<ConfirmParametersPage>();
    }
}
```

**Step 2: Create AdvancedStubView**

Create `demo/MAS/Views/AdvancedStubView.xaml`:
```xml
<UserControl x:Class="MAS.Views.AdvancedStubView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="#F0F0F0">
    <Grid>
        <TextBlock Text="Advanced installation options will be available in a future update."
                   HorizontalAlignment="Center" VerticalAlignment="Center"
                   FontSize="14" Foreground="#666666"/>
    </Grid>
</UserControl>
```

Create `demo/MAS/Views/AdvancedStubView.xaml.cs`:
```csharp
using System.Windows.Controls;

namespace MAS.Views;

public partial class AdvancedStubView : UserControl
{
    public AdvancedStubView() => InitializeComponent();
}
```

**Step 3: Final Program.cs with all pages**

Update `demo/MAS/Program.cs`:
```csharp
using FalkForge.Ui;
using MAS.Pages;
using MAS.Shell;

return InstallerApp.Run(args, app => app
    .Window(w => w.CustomWindow<MasInstallerWindow>())
    .Pages(p => p
        .Add<WelcomePage>()
        .Add<LicensePage>()
        .Add<InstallationTypePage>()
        .Add<DatabaseServerPage>()
        .Add<ConfirmParametersPage>()
        .Add<AdvancedStubPage>()));
```

**Step 4: Build and verify**

Run: `dotnet build demo/MAS/MAS.csproj`
Expected: Build succeeded.

**Step 5: Run the application**

Run: `dotnet run --project demo/MAS/MAS.csproj`
Manual verification:
- Window opens with banner, icon placeholder, "Welcome to MultiAccess setup"
- Click Next → License page with EULA text, accept checkbox enables Next
- Click Next → Installation Type with Standard/Advanced radios
- Select Standard → Next → Database Server page
- Click Next → Confirm Parameters with grouped Name/Value grid
- Click Previous navigates back through each page correctly
- Go back to Installation Type, select Advanced → Next → Advanced Stub page
- Click Next from stub → goes to Confirm Parameters

**Step 6: Commit**

```bash
git add demo/MAS/
git commit -m "add Advanced stub page and complete MAS installer page registration"
```

---

### Task 9: Navigation Polish & Back-Navigation Fix

After testing, fix any navigation issues. Common concerns:

**Files:**
- Potentially modify: `demo/MAS/Pages/InstallationTypePage.cs`
- Potentially modify: `demo/MAS/Pages/ConfirmParametersPage.cs`
- Potentially modify: `demo/MAS/Pages/AdvancedStubPage.cs`
- Potentially modify: `demo/MAS/Pages/DatabaseServerPage.cs`

**Step 1: Verify back-navigation from ConfirmParameters**

The Confirm page's Previous button must go back to the correct page depending on the flow:
- Standard flow: ConfirmParameters → DatabaseServer
- Advanced flow: ConfirmParameters → AdvancedStub

Since `PageResult.Previous` navigates to the previous page by index, and `GoTo<T>` jumps to specific pages, the back navigation should work via the shell's page index tracking. Verify this works correctly.

If `Previous` doesn't navigate correctly when pages were reached via `GoTo<T>`, override `OnBack()`:

```csharp
// In ConfirmParametersPage:
public override PageResult OnBack()
{
    var installType = SharedState.Get<string>("InstallationType");
    return installType == "Advanced"
        ? PageResult.GoTo<AdvancedStubPage>()
        : PageResult.GoTo<DatabaseServerPage>();
}
```

**Step 2: Verify back from AdvancedStub goes to InstallationType**

```csharp
// In AdvancedStubPage (if needed):
public override PageResult OnBack()
    => PageResult.GoTo<InstallationTypePage>();
```

**Step 3: Verify back from DatabaseServer goes to InstallationType**

```csharp
// In DatabaseServerPage (if needed):
public override PageResult OnBack()
    => PageResult.GoTo<InstallationTypePage>();
```

**Step 4: Build, run, and test all navigation paths**

Run: `dotnet build demo/MAS/MAS.csproj && dotnet run --project demo/MAS/MAS.csproj`

Test both flows end-to-end forward and backward.

**Step 5: Commit (if changes were needed)**

```bash
git add demo/MAS/
git commit -m "fix back-navigation for branching flows in MAS installer"
```

---

### Task 10: Final Build + Full Test Suite Verification

**Step 1: Ensure the solution still builds cleanly**

Run: `dotnet build`
Expected: Build succeeded across all projects. 0 Warning(s).

**Step 2: Run all tests**

Run: `dotnet test`
Expected: All ~1900 tests pass. No regressions.

**Step 3: Final commit if any adjustments needed**

---

## Page Registration Order Note

The page registration order in `Program.cs` matters for `PageResult.Previous` (index-based). The order is:
1. WelcomePage (index 0)
2. LicensePage (index 1)
3. InstallationTypePage (index 2)
4. DatabaseServerPage (index 3) — shared by both flows but with different visual variant based on SharedState
5. ConfirmParametersPage (index 4) — Standard goes here after DB page; Advanced goes here last
6. AdvancedInstallDirMultiServerPage (index 5) — Advanced only
7. AdvancedInstallDirMultiServerExPage (index 6) — Advanced only
8. DatabaseConnectionSettingsPage (index 7) — Advanced only
9. MultiServerAdvancedSettingsPage (index 8) — Advanced only
10. MultiServerExAdvancedSettingsPage (index 9) — Advanced only

Since the branching flow uses `GoTo<T>` for forward navigation, and explicit `OnBack()` overrides for backward navigation, the registration order is less important than the GoTo chains.

### Advanced Flow Navigation Chain
```
InstallationType → DatabaseServerPage → AdvancedInstallDirMultiServerPage
    → AdvancedInstallDirMultiServerExPage → DatabaseConnectionSettingsPage
    → MultiServerAdvancedSettingsPage → MultiServerExAdvancedSettingsPage
    → ConfirmParametersPage (Install button)
```

### Standard Flow Navigation Chain
```
InstallationType → DatabaseServerPage → ConfirmParametersPage (Install button)
```

---

### Task 11: Install Directory Pages (MultiServer + MultiServerEx)

**Files:**
- Create: `demo/MAS/Pages/AdvancedInstallDirMultiServerPage.cs`
- Create: `demo/MAS/Views/AdvancedInstallDirMultiServerView.xaml`
- Create: `demo/MAS/Views/AdvancedInstallDirMultiServerView.xaml.cs`
- Create: `demo/MAS/Pages/AdvancedInstallDirMultiServerExPage.cs`
- Create: `demo/MAS/Views/AdvancedInstallDirMultiServerExView.xaml`
- Create: `demo/MAS/Views/AdvancedInstallDirMultiServerExView.xaml.cs`
- Modify: `demo/MAS/Program.cs`

**Step 1: Create AdvancedInstallDirMultiServerPage**

Create `demo/MAS/Pages/AdvancedInstallDirMultiServerPage.cs`:
```csharp
using FalkForge.Ui.Abstractions;
using MAS.Views;

namespace MAS.Pages;

public sealed class AdvancedInstallDirMultiServerPage : MasPageBase<AdvancedInstallDirMultiServerView>
{
    private string _installFolder = @"C:\Program Files (x86)\Aptus\MultiServer";

    public override string Title => "Installation folder for MultiServer";

    public string InstallFolder
    {
        get => _installFolder;
        set => SetField(ref _installFolder, value);
    }

    public override PageResult OnNext()
    {
        SharedState.Set("MultiServerInstallFolder", _installFolder);
        return PageResult.GoTo<AdvancedInstallDirMultiServerExPage>();
    }

    public override PageResult OnBack()
        => PageResult.GoTo<DatabaseServerPage>();
}
```

**Step 2: Create AdvancedInstallDirMultiServerView.xaml**

Create `demo/MAS/Views/AdvancedInstallDirMultiServerView.xaml`:
```xml
<UserControl x:Class="MAS.Views.AdvancedInstallDirMultiServerView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="#F0F0F0">
    <StackPanel Margin="24,24,24,20">
        <TextBlock Text="Install folder" FontSize="12" Margin="0,0,0,4"/>
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <TextBox Grid.Column="0"
                     Text="{Binding InstallFolder, UpdateSourceTrigger=PropertyChanged}"
                     FontSize="12" Padding="4,4" BorderBrush="#AAAAAA"/>
            <Button Grid.Column="1" Content="..." Width="30" Margin="4,0,0,0"
                    Height="26" FontWeight="Bold"/>
        </Grid>
    </StackPanel>
</UserControl>
```

Create `demo/MAS/Views/AdvancedInstallDirMultiServerView.xaml.cs`:
```csharp
using System.Windows.Controls;

namespace MAS.Views;

public partial class AdvancedInstallDirMultiServerView : UserControl
{
    public AdvancedInstallDirMultiServerView() => InitializeComponent();
}
```

**Step 3: Create AdvancedInstallDirMultiServerExPage**

Create `demo/MAS/Pages/AdvancedInstallDirMultiServerExPage.cs`:
```csharp
using FalkForge.Ui.Abstractions;
using MAS.Views;

namespace MAS.Pages;

public sealed class AdvancedInstallDirMultiServerExPage : MasPageBase<AdvancedInstallDirMultiServerExView>
{
    private string _installFolder = @"C:\Program Files (x86)\Aptus\MultiServerEx";

    public override string Title => "Installation folder for MultiServerEx";

    public string InstallFolder
    {
        get => _installFolder;
        set => SetField(ref _installFolder, value);
    }

    public override PageResult OnNext()
    {
        SharedState.Set("MultiServerExInstallFolder", _installFolder);
        return PageResult.GoTo<DatabaseConnectionSettingsPage>();
    }

    public override PageResult OnBack()
        => PageResult.GoTo<AdvancedInstallDirMultiServerPage>();
}
```

**Step 4: Create AdvancedInstallDirMultiServerExView.xaml**

Create `demo/MAS/Views/AdvancedInstallDirMultiServerExView.xaml`:
```xml
<UserControl x:Class="MAS.Views.AdvancedInstallDirMultiServerExView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="#F0F0F0">
    <StackPanel Margin="24,24,24,20">
        <TextBlock Text="Install folder" FontSize="12" Margin="0,0,0,4"/>
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <TextBox Grid.Column="0"
                     Text="{Binding InstallFolder, UpdateSourceTrigger=PropertyChanged}"
                     FontSize="12" Padding="4,4" BorderBrush="#AAAAAA"/>
            <Button Grid.Column="1" Content="..." Width="30" Margin="4,0,0,0"
                    Height="26" FontWeight="Bold"/>
        </Grid>
    </StackPanel>
</UserControl>
```

Create `demo/MAS/Views/AdvancedInstallDirMultiServerExView.xaml.cs`:
```csharp
using System.Windows.Controls;

namespace MAS.Views;

public partial class AdvancedInstallDirMultiServerExView : UserControl
{
    public AdvancedInstallDirMultiServerExView() => InitializeComponent();
}
```

**Step 5: Register pages in Program.cs**

Update `demo/MAS/Program.cs`:
```csharp
using FalkForge.Ui;
using MAS.Pages;
using MAS.Shell;

return InstallerApp.Run(args, app => app
    .Window(w => w.CustomWindow<MasInstallerWindow>())
    .Pages(p => p
        .Add<WelcomePage>()
        .Add<LicensePage>()
        .Add<InstallationTypePage>()
        .Add<DatabaseServerPage>()
        .Add<ConfirmParametersPage>()
        .Add<AdvancedInstallDirMultiServerPage>()
        .Add<AdvancedInstallDirMultiServerExPage>()));
```

**Step 6: Build and verify**

Run: `dotnet build demo/MAS/MAS.csproj`
Expected: Build succeeded.

**Step 7: Commit**

```bash
git add demo/MAS/
git commit -m "add install directory pages for MultiServer and MultiServerEx (Advanced flow)"
```

---

### Task 12: Database Connection Settings Page

**Files:**
- Create: `demo/MAS/Pages/DatabaseConnectionSettingsPage.cs`
- Create: `demo/MAS/Views/DatabaseConnectionSettingsView.xaml`
- Create: `demo/MAS/Views/DatabaseConnectionSettingsView.xaml.cs`
- Modify: `demo/MAS/Program.cs`

**Step 1: Create DatabaseConnectionSettingsPage**

Create `demo/MAS/Pages/DatabaseConnectionSettingsPage.cs`:
```csharp
using FalkForge.Ui.Abstractions;
using MAS.Views;

namespace MAS.Pages;

public sealed class DatabaseConnectionSettingsPage : MasPageBase<DatabaseConnectionSettingsView>
{
    private string _databaseServer = @".\SQLEXPRESS";
    private string _databaseName = "MultiAccess";
    private bool _integratedSecurity = true;
    private string _userName = "AUSR_AptusWeb";
    private string _password = string.Empty;
    private bool _skipTest;

    public override string Title => "Database Connection Settings";
    public override string? Subtitle => "Please enter SQL database information to continue";

    public string DatabaseServer
    {
        get => _databaseServer;
        set => SetField(ref _databaseServer, value);
    }

    public string DatabaseName
    {
        get => _databaseName;
        set => SetField(ref _databaseName, value);
    }

    public bool IntegratedSecurity
    {
        get => _integratedSecurity;
        set
        {
            if (SetField(ref _integratedSecurity, value))
            {
                OnPropertyChanged(nameof(ShowCredentials));
            }
        }
    }

    public bool ShowCredentials => !_integratedSecurity;

    public string UserName
    {
        get => _userName;
        set => SetField(ref _userName, value);
    }

    public string Password
    {
        get => _password;
        set => SetField(ref _password, value);
    }

    public bool SkipTest
    {
        get => _skipTest;
        set => SetField(ref _skipTest, value);
    }

    public string WarningText => "The user will not be created if the user don't exists. For help please see the manual for MultiAccess";

    public override PageResult OnNext()
    {
        SharedState.Set("DbConnectionServer", _databaseServer);
        SharedState.Set("DbConnectionName", _databaseName);
        SharedState.Set("IntegratedSecurity", _integratedSecurity);
        SharedState.Set("DbUserName", _userName);
        SharedState.Set("DbPassword", _password);
        return PageResult.GoTo<MultiServerAdvancedSettingsPage>();
    }

    public override PageResult OnBack()
        => PageResult.GoTo<AdvancedInstallDirMultiServerExPage>();

    public override Task OnNavigatedToAsync()
    {
        // Pre-populate from DB server page if available
        var server = SharedState.Get<string>("DatabaseServer");
        if (!string.IsNullOrEmpty(server))
            DatabaseServer = server;
        var dbName = SharedState.Get<string>("DatabaseName");
        if (!string.IsNullOrEmpty(dbName))
            DatabaseName = dbName;
        return Task.CompletedTask;
    }
}
```

**Step 2: Create DatabaseConnectionSettingsView.xaml**

Create `demo/MAS/Views/DatabaseConnectionSettingsView.xaml`:
```xml
<UserControl x:Class="MAS.Views.DatabaseConnectionSettingsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="#F0F0F0">
    <Grid Margin="20,8,20,8">
        <!-- Database GroupBox -->
        <GroupBox Header="Database" FontSize="12" Padding="12,8">
            <StackPanel>
                <!-- Database server -->
                <TextBlock Text="Database server:" FontSize="12" Margin="0,0,0,4"/>
                <TextBox Text="{Binding DatabaseServer, UpdateSourceTrigger=PropertyChanged}"
                         FontSize="12" Padding="4,4" BorderBrush="#AAAAAA"
                         Margin="0,0,0,8"/>

                <!-- Database name -->
                <TextBlock Text="Name of database:" FontSize="12" Margin="0,0,0,4"/>
                <TextBox Text="{Binding DatabaseName, UpdateSourceTrigger=PropertyChanged}"
                         FontSize="12" Padding="4,4" BorderBrush="#AAAAAA"
                         Margin="0,0,0,8"/>

                <!-- Integrated security -->
                <CheckBox Content="Integrated security"
                          IsChecked="{Binding IntegratedSecurity}"
                          FontSize="12" Margin="0,0,0,8"/>

                <!-- User name -->
                <TextBlock Text="User name:" FontSize="12" Margin="0,0,0,4"/>
                <TextBox Text="{Binding UserName, UpdateSourceTrigger=PropertyChanged}"
                         FontSize="12" Padding="4,4" BorderBrush="#AAAAAA"
                         Margin="0,0,0,2"/>
                <!-- Warning text (yellow background) -->
                <Border Background="#FFFF00" Padding="4,2" Margin="0,0,0,8">
                    <TextBlock Text="{Binding WarningText}" FontSize="11"
                               TextWrapping="Wrap"/>
                </Border>

                <!-- Password -->
                <TextBlock Text="Password:" FontSize="12" Margin="0,0,0,4"/>
                <Grid Margin="0,0,0,8">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <PasswordBox Grid.Column="0" FontSize="12" Padding="4,4"
                                 BorderBrush="#AAAAAA"/>
                    <Button Grid.Column="1" Content="&#x1F441;" Width="30"
                            Margin="4,0,0,0" ToolTip="Show password"/>
                </Grid>

                <!-- Test connection button -->
                <Button Content="Test connection" HorizontalAlignment="Stretch"
                        Padding="8,6" FontSize="12" Margin="0,0,0,4"/>

                <!-- Skip test checkbox -->
                <CheckBox Content="Skip Test"
                          IsChecked="{Binding SkipTest}"
                          FontSize="12"/>
            </StackPanel>
        </GroupBox>
    </Grid>
</UserControl>
```

Create `demo/MAS/Views/DatabaseConnectionSettingsView.xaml.cs`:
```csharp
using System.Windows.Controls;

namespace MAS.Views;

public partial class DatabaseConnectionSettingsView : UserControl
{
    public DatabaseConnectionSettingsView() => InitializeComponent();
}
```

**Step 3: Register page in Program.cs**

Update `demo/MAS/Program.cs` to add `DatabaseConnectionSettingsPage` after `AdvancedInstallDirMultiServerExPage`:
```csharp
using FalkForge.Ui;
using MAS.Pages;
using MAS.Shell;

return InstallerApp.Run(args, app => app
    .Window(w => w.CustomWindow<MasInstallerWindow>())
    .Pages(p => p
        .Add<WelcomePage>()
        .Add<LicensePage>()
        .Add<InstallationTypePage>()
        .Add<DatabaseServerPage>()
        .Add<ConfirmParametersPage>()
        .Add<AdvancedInstallDirMultiServerPage>()
        .Add<AdvancedInstallDirMultiServerExPage>()
        .Add<DatabaseConnectionSettingsPage>()));
```

**Step 4: Build and verify**

Run: `dotnet build demo/MAS/MAS.csproj`
Expected: Build succeeded.

**Step 5: Commit**

```bash
git add demo/MAS/
git commit -m "add Database Connection Settings page with credentials and test connection UI"
```

---

### Task 13: MultiServer Advanced Settings Page

**Files:**
- Create: `demo/MAS/Pages/MultiServerAdvancedSettingsPage.cs`
- Create: `demo/MAS/Views/MultiServerAdvancedSettingsView.xaml`
- Create: `demo/MAS/Views/MultiServerAdvancedSettingsView.xaml.cs`
- Modify: `demo/MAS/Program.cs`

**Step 1: Create MultiServerAdvancedSettingsPage**

Create `demo/MAS/Pages/MultiServerAdvancedSettingsPage.cs`:
```csharp
using FalkForge.Ui.Abstractions;
using MAS.Views;

namespace MAS.Pages;

public sealed class MultiServerAdvancedSettingsPage : MasPageBase<MultiServerAdvancedSettingsView>
{
    private string _dsnName = "MultiAccess";
    private string _serviceAccount = "LocalSystem";
    private string _servicePassword = string.Empty;
    private string _dsnWarning = string.Empty;

    public override string Title => "MultiServer Advanced Settings";

    public string DsnName
    {
        get => _dsnName;
        set
        {
            if (SetField(ref _dsnName, value))
                DsnWarning = string.Empty;
        }
    }

    public string DsnWarning
    {
        get => _dsnWarning;
        set => SetField(ref _dsnWarning, value);
    }

    public string ServiceName => "MultiServer";

    public string ServiceAccount
    {
        get => _serviceAccount;
        set => SetField(ref _serviceAccount, value);
    }

    public string ServicePassword
    {
        get => _servicePassword;
        set => SetField(ref _servicePassword, value);
    }

    public string ServiceWarning => "If the account name is changed the new account must be of type service account to have permission to start MultiServer as a service.";

    public string IntegratedSecurityNote => "If integrated security is used make sure that the service account have correct permissions to the database.";

    public override PageResult OnNext()
    {
        SharedState.Set("MultiServerDsnName", _dsnName);
        SharedState.Set("MultiServerServiceAccount", _serviceAccount);
        SharedState.Set("MultiServerServicePassword", _servicePassword);
        SharedState.Set("MultiServerInstallAsService", true);
        return PageResult.GoTo<MultiServerExAdvancedSettingsPage>();
    }

    public override PageResult OnBack()
        => PageResult.GoTo<DatabaseConnectionSettingsPage>();
}
```

**Step 2: Create MultiServerAdvancedSettingsView.xaml**

Create `demo/MAS/Views/MultiServerAdvancedSettingsView.xaml`:
```xml
<UserControl x:Class="MAS.Views.MultiServerAdvancedSettingsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="#F0F0F0">
    <StackPanel Margin="20,8,20,8">
        <!-- ODBC GroupBox -->
        <GroupBox Header="ODBC" FontSize="12" Padding="12,8" Margin="0,0,0,12">
            <StackPanel>
                <TextBlock Text="DSN Name" FontWeight="Bold" FontSize="12"
                           Margin="0,0,0,4"/>
                <Grid Margin="0,0,0,4">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <TextBox Grid.Column="0"
                             Text="{Binding DsnName, UpdateSourceTrigger=PropertyChanged}"
                             FontSize="12" Padding="4,4" BorderBrush="#AAAAAA"/>
                    <Button Grid.Column="1" Content="Check DSN name."
                            Margin="8,0,0,0" Padding="8,4" FontSize="11"/>
                </Grid>

                <!-- DSN Warning (yellow) -->
                <Border Background="#FFFF00" Padding="4,2" Margin="0,0,0,8"
                        Visibility="{Binding DsnWarning, Converter={StaticResource NullToCollapsedConverter}, FallbackValue=Collapsed}">
                    <TextBlock Text="{Binding DsnWarning}" FontSize="11"
                               TextWrapping="Wrap"/>
                </Border>

                <Button Content="ODBC Data soure administrator"
                        HorizontalAlignment="Center" Padding="12,4"
                        FontSize="12" Margin="0,4,0,0"/>
            </StackPanel>
        </GroupBox>

        <!-- Service GroupBox -->
        <GroupBox Header="Service" FontSize="12" Padding="12,8">
            <StackPanel>
                <TextBlock FontSize="12" Margin="0,0,0,2">
                    <Run Text="Service name"/>
                </TextBlock>
                <TextBlock Text="{Binding ServiceName}" FontWeight="Bold"
                           FontSize="12" Margin="0,0,0,8"/>

                <TextBlock Text="Service Account" FontSize="12" Margin="0,0,0,4"/>
                <TextBox Text="{Binding ServiceAccount, UpdateSourceTrigger=PropertyChanged}"
                         FontSize="12" Padding="4,4" BorderBrush="#AAAAAA"
                         Margin="0,0,0,2"/>

                <!-- Service warning (yellow) -->
                <Border Background="#FFFF00" Padding="4,2" Margin="0,0,0,4">
                    <TextBlock Text="{Binding ServiceWarning}" FontSize="11"
                               TextWrapping="Wrap"/>
                </Border>

                <TextBlock Text="{Binding IntegratedSecurityNote}" FontSize="11"
                           TextWrapping="Wrap" Foreground="#555555"
                           Margin="0,0,0,8"/>

                <!-- Password -->
                <TextBlock Text="Password" FontSize="12" Margin="0,0,0,4"/>
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <PasswordBox Grid.Column="0" FontSize="12" Padding="4,4"
                                 BorderBrush="#AAAAAA"/>
                    <Button Grid.Column="1" Content="&#x1F441;" Width="30"
                            Margin="4,0,0,0" ToolTip="Show password"/>
                </Grid>
            </StackPanel>
        </GroupBox>
    </StackPanel>
</UserControl>
```

Create `demo/MAS/Views/MultiServerAdvancedSettingsView.xaml.cs`:
```csharp
using System.Windows.Controls;

namespace MAS.Views;

public partial class MultiServerAdvancedSettingsView : UserControl
{
    public MultiServerAdvancedSettingsView() => InitializeComponent();
}
```

**Step 3: Register page in Program.cs**

Update Program.cs to add `MultiServerAdvancedSettingsPage`.

**Step 4: Build and verify**

Run: `dotnet build demo/MAS/MAS.csproj`
Expected: Build succeeded.

**Step 5: Commit**

```bash
git add demo/MAS/
git commit -m "add MultiServer Advanced Settings page with ODBC and service configuration"
```

---

### Task 14: MultiServerEx Advanced Settings Page

**Files:**
- Create: `demo/MAS/Pages/MultiServerExAdvancedSettingsPage.cs`
- Create: `demo/MAS/Views/MultiServerExAdvancedSettingsView.xaml`
- Create: `demo/MAS/Views/MultiServerExAdvancedSettingsView.xaml.cs`
- Modify: `demo/MAS/Program.cs`

**Step 1: Create MultiServerExAdvancedSettingsPage**

Create `demo/MAS/Pages/MultiServerExAdvancedSettingsPage.cs`:
```csharp
using FalkForge.Ui.Abstractions;
using MAS.Views;

namespace MAS.Pages;

public sealed class MultiServerExAdvancedSettingsPage : MasPageBase<MultiServerExAdvancedSettingsView>
{
    private string _dsnName = "MultiAccessx64";
    private string _serviceAccount = "LocalSystem";
    private string _servicePassword = string.Empty;
    private string _dsnWarning = string.Empty;

    public override string Title => "MultiServerEx Advanced Settings";

    public string DsnName
    {
        get => _dsnName;
        set
        {
            if (SetField(ref _dsnName, value))
                DsnWarning = string.Empty;
        }
    }

    public string DsnWarning
    {
        get => _dsnWarning;
        set => SetField(ref _dsnWarning, value);
    }

    public string ServiceName => "MultiServerEx";

    public string ServiceAccount
    {
        get => _serviceAccount;
        set => SetField(ref _serviceAccount, value);
    }

    public string ServicePassword
    {
        get => _servicePassword;
        set => SetField(ref _servicePassword, value);
    }

    public string ServiceWarning => "If the account name is changed the new account must be of type service account to have permission to start MultiServer as a service.";

    public string IntegratedSecurityNote => "If integrated security is used make sure that the service account have correct permissions to the database.";

    public override PageResult OnNext()
    {
        SharedState.Set("MultiServerExDsnName", _dsnName);
        SharedState.Set("MultiServerExServiceAccount", _serviceAccount);
        SharedState.Set("MultiServerExServicePassword", _servicePassword);
        SharedState.Set("MultiServerExInstallAsService", true);
        return PageResult.GoTo<ConfirmParametersPage>();
    }

    public override PageResult OnBack()
        => PageResult.GoTo<MultiServerAdvancedSettingsPage>();
}
```

**Step 2: Create MultiServerExAdvancedSettingsView.xaml**

The view is identical structure to MultiServerAdvancedSettingsView but binds to MultiServerEx-specific properties. Create `demo/MAS/Views/MultiServerExAdvancedSettingsView.xaml`:
```xml
<UserControl x:Class="MAS.Views.MultiServerExAdvancedSettingsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             Background="#F0F0F0">
    <StackPanel Margin="20,8,20,8">
        <!-- ODBC GroupBox -->
        <GroupBox Header="ODBC" FontSize="12" Padding="12,8" Margin="0,0,0,12">
            <StackPanel>
                <TextBlock Text="DSN Name" FontWeight="Bold" FontSize="12"
                           Margin="0,0,0,4"/>
                <Grid Margin="0,0,0,4">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <TextBox Grid.Column="0"
                             Text="{Binding DsnName, UpdateSourceTrigger=PropertyChanged}"
                             FontSize="12" Padding="4,4" BorderBrush="#AAAAAA"/>
                    <Button Grid.Column="1" Content="Check DSN name."
                            Margin="8,0,0,0" Padding="8,4" FontSize="11"/>
                </Grid>

                <!-- DSN Warning (yellow) -->
                <Border Background="#FFFF00" Padding="4,2" Margin="0,0,0,8"
                        Visibility="{Binding DsnWarning, Converter={StaticResource NullToCollapsedConverter}, FallbackValue=Collapsed}">
                    <TextBlock Text="{Binding DsnWarning}" FontSize="11"
                               TextWrapping="Wrap"/>
                </Border>

                <Button Content="ODBC Data soure administrator"
                        HorizontalAlignment="Center" Padding="12,4"
                        FontSize="12" Margin="0,4,0,0"/>
            </StackPanel>
        </GroupBox>

        <!-- Service GroupBox -->
        <GroupBox Header="Service" FontSize="12" Padding="12,8">
            <StackPanel>
                <TextBlock FontSize="12" Margin="0,0,0,2">
                    <Run Text="Service name"/>
                </TextBlock>
                <TextBlock Text="{Binding ServiceName}" FontWeight="Bold"
                           FontSize="12" Margin="0,0,0,8"/>

                <TextBlock Text="Service Account" FontSize="12" Margin="0,0,0,4"/>
                <TextBox Text="{Binding ServiceAccount, UpdateSourceTrigger=PropertyChanged}"
                         FontSize="12" Padding="4,4" BorderBrush="#AAAAAA"
                         Margin="0,0,0,2"/>

                <!-- Service warning (yellow) -->
                <Border Background="#FFFF00" Padding="4,2" Margin="0,0,0,4">
                    <TextBlock Text="{Binding ServiceWarning}" FontSize="11"
                               TextWrapping="Wrap"/>
                </Border>

                <TextBlock Text="{Binding IntegratedSecurityNote}" FontSize="11"
                           TextWrapping="Wrap" Foreground="#555555"
                           Margin="0,0,0,8"/>

                <!-- Password -->
                <TextBlock Text="Password" FontSize="12" Margin="0,0,0,4"/>
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <PasswordBox Grid.Column="0" FontSize="12" Padding="4,4"
                                 BorderBrush="#AAAAAA"/>
                    <Button Grid.Column="1" Content="&#x1F441;" Width="30"
                            Margin="4,0,0,0" ToolTip="Show password"/>
                </Grid>
            </StackPanel>
        </GroupBox>
    </StackPanel>
</UserControl>
```

Create `demo/MAS/Views/MultiServerExAdvancedSettingsView.xaml.cs`:
```csharp
using System.Windows.Controls;

namespace MAS.Views;

public partial class MultiServerExAdvancedSettingsView : UserControl
{
    public MultiServerExAdvancedSettingsView() => InitializeComponent();
}
```

**Step 3: Register page and finalize Program.cs with ALL pages**

Update `demo/MAS/Program.cs` with the complete page list:
```csharp
using FalkForge.Ui;
using MAS.Pages;
using MAS.Shell;

return InstallerApp.Run(args, app => app
    .Window(w => w.CustomWindow<MasInstallerWindow>())
    .Pages(p => p
        .Add<WelcomePage>()
        .Add<LicensePage>()
        .Add<InstallationTypePage>()
        .Add<DatabaseServerPage>()
        .Add<ConfirmParametersPage>()
        .Add<AdvancedStubPage>()
        .Add<AdvancedInstallDirMultiServerPage>()
        .Add<AdvancedInstallDirMultiServerExPage>()
        .Add<DatabaseConnectionSettingsPage>()
        .Add<MultiServerAdvancedSettingsPage>()
        .Add<MultiServerExAdvancedSettingsPage>()));
```

**Step 4: Build and verify**

Run: `dotnet build demo/MAS/MAS.csproj`
Expected: Build succeeded.

**Step 5: Commit**

```bash
git add demo/MAS/
git commit -m "add MultiServerEx Advanced Settings page and complete Advanced flow registration"
```

---

### Task 15: Update Navigation for Advanced Flow

**Files:**
- Modify: `demo/MAS/Pages/InstallationTypePage.cs`
- Modify: `demo/MAS/Pages/DatabaseServerPage.cs`
- Modify: `demo/MAS/Pages/ConfirmParametersPage.cs`
- Remove: `demo/MAS/Pages/AdvancedStubPage.cs` (replaced by real Advanced pages)
- Remove: `demo/MAS/Views/AdvancedStubView.xaml`
- Remove: `demo/MAS/Views/AdvancedStubView.xaml.cs`
- Modify: `demo/MAS/Program.cs`

**Step 1: Update InstallationTypePage.OnNext()**

In `demo/MAS/Pages/InstallationTypePage.cs`, the Advanced path should now go to `DatabaseServerPage` (shared page):
```csharp
public override PageResult OnNext()
{
    SharedState.Set("InstallationType", _isStandard ? "Standard" : "Advanced");
    // Both flows go to DatabaseServer next
    return PageResult.Next; // or GoTo<DatabaseServerPage>
}
```

**Step 2: Update DatabaseServerPage navigation**

In `demo/MAS/Pages/DatabaseServerPage.cs`, forward navigation depends on the flow:
```csharp
public override PageResult OnNext()
{
    SharedState.Set("UseExistingDatabase", _useExisting);
    SharedState.Set("DatabaseServer", _databaseServer);
    SharedState.Set("DatabaseName", _databaseName);

    var installType = SharedState.Get<string>("InstallationType");
    return installType == "Advanced"
        ? PageResult.GoTo<AdvancedInstallDirMultiServerPage>()
        : PageResult.GoTo<ConfirmParametersPage>();
}
```

**Step 3: Update ConfirmParametersPage.OnBack()**

In `demo/MAS/Pages/ConfirmParametersPage.cs`, back navigation depends on the flow:
```csharp
public override PageResult OnBack()
{
    var installType = SharedState.Get<string>("InstallationType");
    return installType == "Advanced"
        ? PageResult.GoTo<MultiServerExAdvancedSettingsPage>()
        : PageResult.GoTo<DatabaseServerPage>();
}
```

**Step 4: Update ConfirmParametersPage.OnNavigatedToAsync() for Advanced data**

Expand `OnNavigatedToAsync()` to include Advanced flow parameters when `InstallationType == "Advanced"`:
- MultiServer install folder
- MultiServerEx install folder
- Database connection settings (server, db, integrated security, user, password)
- MultiServer advanced settings (DSN, service account, install as service)
- MultiServerEx advanced settings (DSN, service account, install as service)

**Step 5: Remove AdvancedStubPage files and update Program.cs**

Delete the stub page files and remove `AdvancedStubPage` from Program.cs registration.

**Step 6: Build, run, and test all navigation paths**

Run: `dotnet build demo/MAS/MAS.csproj && dotnet run --project demo/MAS/MAS.csproj`

Test Standard flow: Welcome → License → InstallType (Standard) → DB → Confirm → Install
Test Advanced flow: Welcome → License → InstallType (Advanced) → DB → InstallDir MS → InstallDir MSEx → DB Connection → MS Advanced → MSEx Advanced → Confirm → Install
Test back navigation through both complete flows.

**Step 7: Commit**

```bash
git add demo/MAS/
git commit -m "wire Advanced flow navigation and remove AdvancedStubPage"
```

---

### Task 16: Final Build + Full Test Suite Verification

**Step 1: Ensure the solution still builds cleanly**

Run: `dotnet build`
Expected: Build succeeded across all projects. 0 Warning(s).

**Step 2: Run all tests**

Run: `dotnet test`
Expected: All ~1900 tests pass. No regressions from the demo addition.

**Step 3: Final commit if any adjustments needed**
