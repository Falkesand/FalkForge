# MAS (MultiAccess Setup) Custom Bundle Installer

## Overview
Custom WPF bundle installer demo for "MultiAccess 8.9.0" using FalkForge's custom UI framework. Classic Windows installer look with a custom window shell providing banner area, version string, and configurable button bar.

## Project Location
`demo/MAS/`

## Pages (Standard Flow)

### Shared Pages (1-3)
1. **WelcomePage** - Welcome text, Next/Cancel only (no Previous)
2. **LicensePage** - Scrollable EULA, "I accept" checkbox gates Next, Print button
3. **InstallationTypePage** - Standard/Advanced radio selection with side-by-side description panels

### Standard Flow Pages (4-5)
4. **DatabaseServerPage** - Use existing/Create empty database radios, server textbox (.\SQLEXPRESS), database name textbox (MultiAccess)
5. **ConfirmParametersPage** - Name/Value grid grouped by category, Install button instead of Next

### Advanced Flow (stub)
6. **AdvancedStubPage** - Placeholder "Coming soon" text

## Architecture

### Custom Window Shell (`MasInstallerWindow`)
Replaces default `CustomInstallerWindow` via `.CustomWindow<MasInstallerWindow>()`.

Layout:
- **Title bar**: Standard Windows chrome, "Installer for MultiAccess 8.9.0"
- **Banner area**: Gray gradient (#E8E8E8 → #D0D0D0), orange-red placeholder ellipse icon, Title + optional Subtitle
- **Separator line**: #A0A0A0
- **Content area**: Page view hosted in ContentPresenter
- **Button bar**: Version string left (#808080), Previous/Next/Cancel right (75x23px standard buttons)

### Page Properties for Shell
Each page exposes properties the shell binds to:
- `Title` (string) - Banner title
- `Subtitle` (string?) - Optional subtitle line
- `NextButtonText` (string) - Default "Next", "Install" on confirm page
- `ShowPrintButton` (bool) - True only on LicensePage
- `ShowPreviousButton` (bool) - False on WelcomePage

### Navigation Flow
```
Welcome → License → InstallationType
                         │
               ┌─────────┴─────────┐
           Standard             Advanced
               │                    │
        DatabaseServer        AdvancedStub
               │               (placeholder)
       ConfirmParameters           │
          (Install btn)      ConfirmParameters
                                (Install btn)
```

### SharedState Keys
- `InstallationType` - "Standard" or "Advanced"
- `UseExistingDatabase` - bool
- `DatabaseServer` - string (default ".\SQLEXPRESS")
- `DatabaseName` - string (default "MultiAccess")

## Visual Theme (MasTheme.xaml)
Classic Windows installer aesthetic:
- `BannerBackground`: LinearGradient #E8E8E8 → #D0D0D0
- `ContentBackground`: #F0F0F0
- `BannerSeparator`: #A0A0A0
- `VersionForeground`: #808080
- `IconBrush`: #CC3300 (orange-red placeholder)
- Buttons: Standard Windows style, 75x23px

## File Structure
```
demo/MAS/
  MAS.csproj
  Program.cs
  Shell/
    MasInstallerWindow.xaml
    MasInstallerWindow.xaml.cs
  Pages/
    WelcomePage.cs
    LicensePage.cs
    InstallationTypePage.cs
    DatabaseServerPage.cs
    ConfirmParametersPage.cs
    AdvancedStubPage.cs
  Views/
    WelcomeView.xaml
    LicenseView.xaml
    InstallationTypeView.xaml
    DatabaseServerView.xaml
    ConfirmParametersView.xaml
    AdvancedStubView.xaml
  Themes/
    MasTheme.xaml
```

## Decisions
- Custom window shell for consistent banner across all pages
- Shell-managed button bar with per-page customization via properties
- Simple placeholder icon (orange ellipse) instead of XAML path data
- Stub pages for Advanced flow (coming later)
- Focus on visual fidelity and Next/Previous navigation working correctly
