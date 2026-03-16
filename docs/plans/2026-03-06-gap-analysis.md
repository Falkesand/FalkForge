# FalkForge Gap Analysis vs Industry — March 2026

## Current Strengths

FalkForge is competitive with WiX Toolset and has advantages in DX (C# fluent API vs XML), decompilation, visual editor (Studio), and supply chain features (SBOM, WinGet, reproducible builds, dry-run).

| Area | Status |
|------|--------|
| MSI compilation | Full-featured, on par with WiX |
| EXE bootstrapper/bundles | Full Burn-equivalent with chain, rollback, download |
| Extensions (IIS, SQL, Firewall, XML, ODBC, etc.) | Comparable to WiX extensions |
| C# fluent API | Better DX than WiX XML |
| Custom UI (WPF) | Better than WiX Burn BA |
| Decompiler (MSI + Bundle) | Unique advantage |
| SBOM / WinGet / Reproducible builds | Ahead of all competitors |
| Dry-run simulation | Unique feature |
| Merge modules, patches, transforms | Full support |
| Localization | JSON-based, modern approach |
| Elevation / Restart Manager / Rollback | Production-quality engine |
| Visual Studio-free (CLI + Studio) | Good CI/CD story |

## Gaps (Priority Ordered)

### Critical — Expected by enterprise customers

| # | Gap | Adv Installer | InstallShield | WiX | Effort |
|---|-----|---------------|---------------|-----|--------|
| 1 | **MSIX output** | Yes | Yes | No | High |
| 2 | **Auto-updater runtime** | Built-in | FlexNet | Basic | Medium |
| 3 | **ICE validation** | Yes | Yes | Yes | Medium |
| 4 | **Driver installation** | Yes | Yes | No | Low |

### Important — Competitive differentiation

| # | Gap | Adv Installer | InstallShield | WiX | Effort |
|---|-----|---------------|---------------|-----|--------|
| 5 | **Repackager** (EXE → MSI capture) | Yes | Yes | No | High |
| 6 | **Delta/differential updates** | Yes | No | No | High |
| 7 | **Installer analytics/telemetry** | Yes | Via Flexera | No | Medium |
| 8 | **Serial/license validation** | Yes | Via Flexera | No | Low |
| 9 | **COM/COM+ registration API** | Yes | Yes | Yes | Low |
| 10 | **PowerShell custom actions** | Yes | Yes | No | Low |

### Nice-to-Have — Polish and ecosystem

| # | Gap | Notes | Effort |
|---|-----|-------|--------|
| 11 | VM testing from IDE | Launch installer in VM from Studio | High |
| 12 | Import from WiX/NSIS/Inno | Convert competitor projects | Medium |
| 13 | MSI raw table editor | Direct table editing in Studio | Low |
| 14 | Multiple instances | Same product, different configs | Medium |
| 15 | Streaming install | Progressive install while downloading | High |
| 16 | App-V output | Declining format, low priority | High |

## Recommended Execution Order

1. **MSIX output** — Biggest gap for enterprise/modern Windows
2. **ICE validation** — Table-stakes for MSI quality
3. **Auto-updater runtime** — Feed infrastructure exists, needs updater EXE
4. **PowerShell custom actions** — Quick win, high demand
5. **COM/COM+ registration API** — Quick win
6. **Driver installation API** — Quick win via DIFxApp
7. **Serial/license validation** — Competitive differentiator
8. **Delta updates** — Reduce update download sizes
9. **Repackager** — Enterprise workflow
10. **Installer analytics** — SaaS opportunity
