# FalkForge — Competitive Gap Analysis & Roadmap

**Date:** 2026-07-10
**Full report (styled):** [`2026-07-10-competitive-gap-analysis.html`](./2026-07-10-competitive-gap-analysis.html) — open in a browser for the readable version with tables and priority badges.
**Method:** internal capability inventory + web research across Windows installer tools (WiX v4-6, Advanced Installer, InstallShield, Inno, NSIS, MSIX), modern update/cross-platform frameworks (Velopack, Conveyor, Tauri, electron-builder, Sparkle), package managers (WinGet, Chocolatey), and developer-experience/ecosystem sources.

## Framing

FalkForge is engineered **deeper than any competitor on supply-chain/trust** (hybrid post-quantum signing, key quorum, reproducible builds, SBOM, a provable plan/verify pipeline — none of WiX/Advanced Installer/InstallShield have these). The gaps are **not** in the engine. They are in **adoption surface, the update lifecycle, and developer onboarding** — the parts that decide whether anyone gets far enough to benefit from the engine. Built like a 10/10 engine, shipped like a 0/10 product (private repo, no NuGet, no docs site, no license, no templates).

Strategic timing: WiX v6+ introduced an Open-Source Maintenance Fee (2025); the community is actively reassessing installers. `forge migrate` (MSI/WiX-Burn → buildable C#) is a ready-made on-ramp for those refugees — currently unmarketed.

Priority legend: **[QW]** quick win (low effort, high return) · **[WD]** worth doing (medium) · **[BB]** big bet (high effort, decide deliberately).

---

## ⭐ Start here — highest-leverage, lowest-effort (do these first)

- [ ] **[QW] Ship it as a product** — publish NuGet packages, split `documentation.html` into a searchable/SEO-indexed docs site, state a license, open GitHub Discussions. No distribution = no adoption.
- [ ] **[QW] `forge init` + `dotnet new` templates** — point at a publish folder → get a working, buildable installer project. Velopack proved a <15-minute first-installer wins developers.
- [ ] **[QW] Official CI** — `setup-falkforge` GitHub Action + an Azure DevOps task + a documented build→sign→publish→WinGet-PR workflow.
- [ ] **[QW] `forge preview`** — live installer-UI hot-reload against the existing `NullInstallerEngine` with theme hot-reload. No competitor has hot-reload installer UI.
- [ ] **[WD] `forge sandbox` / `forge test`** — generate a Windows Sandbox `.wsb`, run install→upgrade→uninstall, assert no leftover files/registry, exit red/green for CI. Top unmet market wish; extends provability to runtime.

## A · Update lifecycle (biggest capability gap)

The feed today answers only "newer + delta + signed?". Competitors treat updates as a lifecycle.

- [ ] **[QW] Staged/percentage rollout** — per-install ID hash vs a `StagedRolloutPercentage` in the feed entry. Catch a bad release at 10% of users, not 100%.
- [ ] **[QW] Release channels** — stable/beta/nightly, per-channel feed files, runtime channel switch.
- [ ] **[WD] Update-policy knobs** — criticality/mandatory flag, check cadence, force-update-before-launch, OS-scheduled background check (Task Scheduler, no app running).
- [ ] **[WD] Vendor-authorized rollback/downgrade** — a signed "pull this release" token that co-operates with the anti-downgrade epoch (design care required so it doesn't weaken epoch security).

## B · Distribution & package-manager reach

- [ ] **[QW] WinGet auto-submission** — `forge winget --submit` via wingetcreate/Komac + a shipped GH Action. Manifest is generated but not submitted today.
- [ ] **[QW] Chocolatey package emitter** — nuspec + silent-install script from the PackageModel.
- [ ] **[WD] Download-site scaffold** — OS/arch-detecting download page + feed + bootstrap script (Conveyor-style).
- [ ] **[WD] Intune deployment kit** — `.intunewin` + detection rules + a documented silent-switch/exit-code contract for the bundle EXE. (Note: end-user silent mode for the bundle EXE is authoring-time only today — needs a real `/quiet /norestart` + 0/3010 contract.)

## C · Authoring DX & tooling

- [ ] **[WD] Roslyn analyzers for authoring** — the PKG/FEA/SVC validators as live editor squiggles + code fixes. Unique to code-first; XML/script tools can't do it.
- [ ] **[WD] Visual Studio extension** — templates, F5 build/debug of an installer project (à la WiX HeatWave).
- [ ] **[BB] Studio: theme editor + live sequence preview** — colors/branding/watermark + preview, not a full control-level dialog designer. Competitors paywall visual designers; a free one is a differentiator.

## D · Signing & CI (already strong — small gaps)

- [ ] **[QW] Azure Trusted Signing provider** — next to the existing SignServer provider. What indie ISVs use in CI now.
- [ ] **[QW] SPDX SBOM** — currently CycloneDX only; some procurement pipelines require SPDX.
- [ ] **[QW] Release-notes/changelog embedding** — into the bundle + update feed entry (feeds the download-site scaffold and "what's new" prompt).

## E · Diagnostics & end-user UX

- [ ] **[QW] "What will this change" page for end users** — expose `forge plan` (files/services/registry preview + ETA) in the installer UI. Nobody shows users a verifiable change manifest.
- [ ] **[QW] UI language packs** — ship 30-60 translations (currently en-US + sv-SE). Mechanism exists; content grind.
- [ ] **[WD] `forge logs analyze` + support-bundle** — parse engine + MSI verbose logs → known failure signatures + fix hints; one-click zip of logs+plan+machine facts. (wilogutl is 20 years stale.)
- [ ] **[WD] End-user UX pass** — system dark-mode auto-detect in the default theme, UIA accessibility names (also unlocks UI test automation), keyboard-only flow, per-monitor-v2 DPI.
- [ ] **[BB] Opt-in install analytics** — success/failure/error-code beacon → dashboard-ready feed (opt-in default). Advanced Installer sells this as a paid product.

## F · Bigger strategic bets (high effort — decide deliberately)

- [ ] **[WD] Prerequisite/runtime catalog** — curated VC++ redist, .NET, WebView2, SQL Express, Java… with detection logic + official URLs, embed-or-download. Fits the existing bundle chain + .NET-detection extension.
- [ ] **[BB] Finish MSIX** — CLI dispatch + `.appinstaller` generation + Package Support Framework + Store submission API. Compiler exists but is experimental.
- [ ] **[BB] Trial/licensing engine** — signed serials, trial period, web validation, blacklist. ISVs pay Advanced Installer specifically for this; the crypto stack is a head start.
- [ ] **[BB] Cross-platform via compile-only emitters** — `.deb`/`.rpm`/AppImage, then macOS `.pkg`/`.dmg`, that ride native updaters (Sparkle appcast, Flathub metadata) instead of porting the engine. Conveyor's whole moat; the model layer is ~80% OS-agnostic already. `.deb`/`.rpm` are pure archive-writing on Windows; macOS notarization can reuse the remote-signing infra (rcodesign needs no Mac).
- [ ] **[BB] Repackaging/setup capture** — snapshot/monitor a legacy EXE installer → MSI/MSIX (sandbox/VM capture). Opens the IT-pro/migration market.
- [ ] **[BB] Niche** — encrypted/passworded installers (Inno), multi-instance installs (InstallShield), per-user no-UAC install/update mode (Velopack), a lightweight non-MSI file-copy package + smaller engine footprint (Inno/NSIS audience).

## Loose ends (quick fixes)

- [ ] Stale `README` — says 57 demos / 2,500 tests / 25 projects; reality is 63 / ~3,500 / 28.
- [ ] MSIX CLI gap and the design-time placeholder engine stub in `forge build` bundles (both honesty-flagged, but limit what the CLI ships).
- [ ] Studio has no demo/walkthrough — a single "build this installer in Studio" guide would de-risk it.

## One-sentence advice

Stop adding to the engine and start shipping the product: publish it, template the on-ramp, wire the CI, put `forge migrate` in front of the WiX-fee refugees — then turn the update *feed* into an update *lifecycle*. The security depth is the differentiator; the market just can't see it yet.
