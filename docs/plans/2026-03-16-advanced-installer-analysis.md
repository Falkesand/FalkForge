# Competitive Analysis: Advanced Installer vs FalkForge Studio

## Overview

Analysis of Advanced Installer's UI, features, and approach to installer building. Used to inform the Studio roadmap.

**Sources:**
- [Advanced Installer Gallery](https://www.advancedinstaller.com/gallery.html) — 57 screenshots across all editors
- [Product Details Tab](https://www.advancedinstaller.com/user-guide/product-details-tab.html)
- [Feature Properties](https://www.advancedinstaller.com/user-guide/feature-properties.html)
- [Feature List](https://www.advancedinstaller.com/feats-list.html)

---

## Product Details Editor

Advanced Installer's Product page has **5 tabs** with ~20 fields total:

### General Tab
- Product Name (PseudoFormatted, localizable, Smart Edit Control)
- Version (x.y.z, max 255.255.65535.65535, auto-fetch from project/disk/INI)
- Publisher (PseudoFormatted, localizable)

### Support Info
- Support URL → `ARPHELPLINK`
- Update Info URL → `ARPURLINFOABOUT`
- Help URL → `ARPHELPTELEPHONE`
- Phone → `ARPHELPTELEPHONE`
- Email → `ARPCONTACT`
- Comments → `ARPCOMMENTS`

### Programs & Features
- Register with Windows Installer (checkbox, with caution warning)
- Custom icon picker (browse .ico, dropdown, reset)
- Disable Modify checkbox → `ARPNOMODIFY`
- Disable Repair checkbox → `ARPNOREPAIR`
- Disable Remove checkbox → `ARPNOREMOVE`
- Do not show in list → `ARPSYSTEMCOMPONENT`
- Override entry name + unified uninstall/change button

### EULA
- License file (RTF/URL, auto-adds InstallDlg)

### Readme
- Show in Readme Dialog (adds ReadmeDlg)
- Show in Control Panel (ReadMe in Add/Remove Programs)

**FalkForge Studio currently has:** 8 fields on a single flat page (Name, Manufacturer, Version, Upgrade Code, Architecture, Scope, Description, Output Format). No tabs, no support info, no Programs & Features controls.

---

## Feature Properties

Advanced Installer's feature editor includes:

- **Install Levels:** Integer-based (0=Disabled, 1=Typical, 4=Complete) with presets
- **Conditional Levels:** Dialog for custom install level configurations
- **Feature Tree Display:** Not Displayed / Collapsed / Expanded
- **Per-feature directory:** User-defined directory with uppercase identifier
- **Per-feature icon:** Prepended in Quick Selection List
- **Component marking:** 64-bit, prevent registration, permanent
- **Install settings:** Local install / Run from source / Follow parent / Mandatory
- **Advertise settings:** Control advertised mode availability
- **Build inclusion:** Include in all builds or select specific builds

**FalkForge Studio currently has:** ID, Title, Description, Default (checkbox), Required (checkbox). No install levels, no tree display control, no icons, no conditional logic.

---

## Full Feature Scope (57 Screenshot Categories)

### What Advanced Installer Has That We Also Have
| Feature | AI | FalkForge | Notes |
|---------|----|----|-------|
| MSI creation | ✓ | ✓ | Core capability |
| EXE Bundle | ✓ (Suite Installer) | ✓ | |
| MSIX packaging | ✓ | ✓ | |
| Files & folders | ✓ | ✓ | |
| Registry | ✓ | ✓ | |
| Services | ✓ | ✓ | |
| Shortcuts | ✓ | ✓ | |
| Environment variables | ✓ | ✓ | |
| Custom actions | ✓ | ✓ | |
| SQL databases | ✓ | ✓ | |
| IIS configuration | ✓ | ✓ (API only) | Not in Studio |
| Firewall rules | ✓ | ✓ | |
| XML config | ✓ | ✓ | |
| ODBC | ✓ | ✓ | |
| Digital signing | ✓ | ✓ | API/CLI only, not in Studio |
| Auto-updater | ✓ | ✓ | Just completed |
| Localization | ✓ (31 languages) | ✓ (en-US, sv-SE) | |
| Feature tree | ✓ | ✓ | |
| Merge modules | ✓ | ✓ | API only |
| Patches (MSP) | ✓ | ✓ | API only |
| Transforms (MST) | ✓ | ✓ | API only |
| Decompiler | ✓ | ✓ | CLI only |

### What Advanced Installer Has That We Don't
| Feature | Priority | Notes |
|---------|----------|-------|
| **75+ installer themes** | High | We have 5 dialog templates |
| **Visual dialog editor (WYSIWYG)** | High | We have no visual dialog editing |
| **Project templates / wizards** | High | We start with empty project |
| **Repackager** (capture installs) | Medium | Enterprise feature, complex |
| **Serial code validation** | Medium | Niche but common ask |
| **Prerequisites / dependency download** | Medium | We have DependencyExtension but no download UI |
| **CI/CD integration UI** | Medium | We have CLI but no pipeline generation |
| **Direct MSI table editor** | Medium | We have Decompiler as read-only alternative |
| **Patch creation wizard** | Low | We have PatchCompiler API |
| **App-V packaging** | Low | Legacy format |
| **VM profiles for testing** | Low | Nice-to-have |
| **Games Explorer** | Low | Niche, deprecated by Microsoft |
| **CD/DVD AutoRun** | Low | Obsolete |
| **SCCM deployment** | Low | Enterprise, can use MSI directly |

---

## Key UI/UX Patterns

### Navigation
- Left sidebar tree with **grouped categories** (Product Information, Resources, Installation, etc.)
- Each category expands to show sub-pages
- We have a flat tree — no grouping

### Editor Layout
- **Tabbed editors** for complex pages (Product has 5 tabs)
- Grid layouts with label-above-input pattern
- Inline help via tooltips and description text (similar to what we just added)
- "More Options" hyperlinks that expand advanced settings
- We have single-page editors with no tabs

### Build & Output
- Structured build output with progress
- Error/warning list with navigation
- We have text-only output

### Themes & Branding
- Theme gallery with visual preview thumbnails
- Banner/watermark customization with live preview
- We have dialog template selection as a dropdown

---

## Strategic Takeaways

1. **Editor richness is the #1 gap.** Product editor alone needs 3x more fields. Feature editor needs install levels. This is Phase 1 of the roadmap.

2. **Workflow features matter more than advanced features.** Live validation, project templates, and undo/redo would have more daily impact than a visual dialog editor.

3. **Navigation grouping** would immediately make Studio feel more professional — group tree nodes into categories like Advanced Installer does.

4. **We already match on core capabilities.** MSI/Bundle/MSIX compilation, extensions, decompiler, auto-updater — the engine is strong. The gap is the GUI layer.

5. **Don't chase everything.** Repackager, App-V, Games Explorer, CD AutoRun are niche/legacy. Focus on what modern users need: good editors, fast builds, CI/CD export.
