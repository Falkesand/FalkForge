# Tutorials Design

**Goal:** Create a tutorial site that guides three audiences (.NET developers, IT/DevOps, WiX migrants) from zero to productive with FalkForge.

**Architecture:** Hub-and-spoke. One "Getting Started" tutorial everyone does first, then self-contained topic tutorials you pick based on need. Each tutorial wraps an existing demo — no duplicate code to maintain.

**Format:** Standalone HTML pages in `docs/tutorials/` with shared CSS/JS. Dark/light theme matching documentation.html. Collapsible `<details>` sections for deep-dives on MSI concepts.

---

## Directory Layout

```
docs/tutorials/
  index.html              # Hub page with learning paths
  shared/
    tutorial.css          # Dark/light theme, code blocks, collapsible styling
    tutorial.js           # Theme toggle, code highlighting
  getting-started.html    # Wraps demo 01 — first 5 minutes
  msi-basics.html         # Wraps demos 01-05
  services.html           # Wraps demo 17
  custom-ui.html          # Wraps demos 11-13
  bundles.html            # Wraps demos 06, 35-43
  extensions.html         # Wraps demos 29-34
  msix.html               # Wraps demos 15, 52
  advanced-msi.html       # Wraps demo 09 (custom actions, tables, MSM, MSP, MST)
  json-config.html        # Wraps json/ demos
  localization.html       # Wraps demo 08
  coming-from-wix.html    # Migration guide with side-by-side translations
```

## Tutorial Page Template

Each tutorial follows this structure:

1. **Header** — Title, one-sentence description, reading time, prerequisites
2. **What you'll build** — One paragraph describing the end result
3. **The code** — Walk through the demo's Program.cs top to bottom. Each chunk is 5-10 lines with plain-English explanation. No jargon without defining it first.
4. **Collapsible deep-dives** — `<details><summary>` blocks after each concept. Closed by default. Main flow stays clean and fast.
5. **Try it** — Build command + what to expect
6. **What's next** — Links to related tutorials and full demo README

## Writing Style

- Short sentences. Active voice. Present tense.
- Explain every concept the first time it appears.
- No assumed MSI knowledge. First tutorial explains what an MSI is.
- Code snippets reference actual demo files with line numbers.
- Collapsible sections for anyone who wants to go deep.

## Getting Started Tutorial

Wraps `demo/01-hello-world`. Under 5 minutes. Covers:

1. What is FalkForge? (two sentences)
2. Install (add NuGet reference)
3. Walk through Program.cs line by line with collapsible deep-dives for each concept
4. Build and inspect with `forge inspect`
5. What's next — three paths by audience:
   - .NET developer → MSI Basics
   - IT/DevOps → JSON Config
   - Coming from WiX → Migration guide

## Coming from WiX Migration Guide

Side-by-side translation format (WiX XML left, FalkForge C# right):

1. Concepts mapping table (Package.wxs → Program.cs, Fragment → methods, etc.)
2. Common patterns — 10 things every WiX project does, translated
3. Extension mapping — WiX extension → FalkForge extension table
4. Bundle migration — Burn → BundleBuilder with security highlights
5. What you gain — honest facts, no FUD
6. Decompile your existing MSI — `forge decompile` as a practical first step

## Phasing

**Phase 1 (ship first):**
- `index.html` + `shared/` (CSS/JS)
- `getting-started.html` (demo 01)
- `msi-basics.html` (demos 01-05)
- `coming-from-wix.html` (migration guide)

**Phase 2:**
- `custom-ui.html` (demos 11-13)
- `bundles.html` (demos 06, 35-43)
- `json-config.html` (json/ demos)
- `extensions.html` (demos 29-34)

**Phase 3:**
- `msix.html` (demos 15, 52)
- `advanced-msi.html` (demo 09)
- `localization.html` (demo 08)
- `services.html` (demo 17)

Index page ships with Phase 1. Phase 2/3 entries shown as "Coming soon."
