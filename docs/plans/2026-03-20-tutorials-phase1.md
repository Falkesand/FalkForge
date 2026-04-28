# Tutorials Phase 1 Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build the tutorial hub page, shared theme, and three tutorials (Getting Started, MSI Basics, Coming from WiX) that guide all three audiences to productivity.

**Architecture:** Each tutorial is a standalone HTML page sharing CSS/JS extracted from the existing documentation.html theme. Tutorials wrap existing demo code — no new C# to maintain. Collapsible `<details>` sections for deep-dives.

**Tech Stack:** HTML5, CSS3 (CSS variables, dark/light theme), vanilla JavaScript (theme toggle, no frameworks).

---

### Task 1: Create shared CSS

**Files:**
- Create: `docs/tutorials/shared/tutorial.css`

**Step 1: Create the CSS file**

Extract and adapt the theme from `docs/gen/header.html`. The CSS must include:

1. **CSS reset + variables** — Copy both `[data-theme="dark"]` and `[data-theme="light"]` variable blocks from `docs/gen/header.html` lines 8-98 verbatim. These define all colors, syntax highlighting, and spacing.

2. **Base styles** — Body, headings (h1-h4), paragraphs, links, code blocks, inline code. Use the CSS variables for all colors. Font: `-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif`. Code font: `'Cascadia Code', 'Fira Code', Consolas, monospace`.

3. **Layout** — Single-column centered layout. Max-width 800px. Padding 2rem sides. No sidebar (tutorials are simpler than the reference docs).

4. **Header bar** — Fixed top bar with FalkForge logo text, "Tutorials" label, theme toggle button (sun/moon icon), and "Back to Docs" link. Background: `var(--bg-sidebar)`. Height: 56px.

5. **Code blocks** — `pre > code` with background `var(--bg-code)`, border `var(--border)`, border-radius 6px, padding 1.25rem, overflow-x auto. Syntax highlighting classes: `.kw` (keyword), `.str` (string), `.cmt` (comment), `.typ` (type), `.num` (number), `.method` (method), `.punc` (punctuation).

6. **Collapsible deep-dives** — Style `<details class="deep-dive">`:
   - `summary`: cursor pointer, color `var(--accent)`, font-weight 600, padding 0.5rem 0, user-select none. On hover: color `var(--link-hover)`.
   - When open: border-left 3px solid `var(--accent)`, padding-left 1rem, margin-left 0.5rem, background `var(--bg-secondary)`, border-radius 0 6px 6px 0, padding 1rem.
   - Summary marker: triangle that rotates 90deg when open.

7. **Tutorial metadata** — `.tutorial-meta` class for the header block showing reading time, prerequisites. Muted text, small font, border-bottom.

8. **Side-by-side comparison** — `.comparison` grid: two-column at desktop (min 700px), single-column on mobile. `.comparison-left` and `.comparison-right` with labels "WiX" / "FalkForge".

9. **Note and warning callouts** — `.note` and `.warning` classes matching the docs theme (colored left border + background).

10. **Tables** — Striped rows with `var(--bg-table-alt)`, header with `var(--bg-table-header)`.

11. **Navigation footer** — `.tutorial-nav` flex container at bottom of each tutorial. "Previous" on left, "Next" on right. Styled as bordered pill buttons.

12. **Responsive** — At `max-width: 768px`, reduce padding, make comparison columns stack, reduce font sizes slightly.

**Step 2: Verify by opening a test HTML file in browser**

Create a minimal `docs/tutorials/_test.html` that includes the CSS and has example elements (heading, code block, details, comparison, note). Open in browser, verify dark/light themes both work. Delete the test file after verification.

**Step 3: Commit**

```
docs(tutorials): add shared tutorial CSS with dark/light theme
```

---

### Task 2: Create shared JavaScript

**Files:**
- Create: `docs/tutorials/shared/tutorial.js`

**Step 1: Create the JS file**

Minimal vanilla JS for:

1. **Theme toggle** — Read from `localStorage` key `falkforge-theme`. Default to dark. Toggle `data-theme` attribute on `<html>`. Store choice. Same localStorage key as documentation.html so theme preference is shared.

2. **Smooth scroll** — Scroll to anchor links smoothly.

3. **Code copy button** — Add a "Copy" button to each `pre > code` block. On click, copy text content to clipboard, show "Copied!" for 2 seconds.

That's it. No search, no sidebar, no intersection observer — tutorials are simple single-page documents.

**Step 2: Commit**

```
docs(tutorials): add shared tutorial JavaScript
```

---

### Task 3: Create index.html (hub page)

**Files:**
- Create: `docs/tutorials/index.html`

**Step 1: Create the hub page**

Structure:

```html
<!DOCTYPE html>
<html lang="en" data-theme="dark">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>FalkForge Tutorials</title>
  <link rel="stylesheet" href="shared/tutorial.css">
</head>
<body>
  <header class="top-bar">
    <span class="logo">FalkForge</span>
    <span class="label">Tutorials</span>
    <nav>
      <a href="../documentation.html">Reference Docs</a>
      <button id="theme-toggle" title="Toggle theme">🌙</button>
    </nav>
  </header>

  <main>
    <h1>Learn FalkForge</h1>
    <p>Build Windows installers from C# code. These tutorials walk you through real demo projects, explaining every concept along the way.</p>

    <section>
      <h2>Start Here</h2>
      <div class="tutorial-card">
        <a href="getting-started.html">Getting Started</a>
        <p>Build your first MSI installer in 5 minutes.</p>
        <span class="meta">5 min · No prerequisites</span>
      </div>
    </section>

    <section>
      <h2>Topic Tutorials</h2>
      <!-- Each is a tutorial-card with link, description, reading time -->
      <div class="tutorial-card">
        <a href="msi-basics.html">MSI Basics</a>
        <p>Files, shortcuts, services, registry, and features — the building blocks of every installer.</p>
        <span class="meta">20 min · Prerequisite: Getting Started</span>
      </div>
      <div class="tutorial-card coming-soon">
        <span>Custom UI</span>
        <p>Build WPF installer interfaces with pages, navigation, and theming.</p>
        <span class="meta">Coming soon</span>
      </div>
      <div class="tutorial-card coming-soon">
        <span>EXE Bundles</span>
        <p>Package multiple MSIs into a single self-extracting installer.</p>
        <span class="meta">Coming soon</span>
      </div>
      <div class="tutorial-card coming-soon">
        <span>Extensions</span>
        <p>Firewall rules, IIS, SQL Server, .NET detection, and more.</p>
        <span class="meta">Coming soon</span>
      </div>
      <div class="tutorial-card coming-soon">
        <span>JSON Configuration</span>
        <p>Define installers in JSON without writing C#.</p>
        <span class="meta">Coming soon</span>
      </div>
      <div class="tutorial-card coming-soon">
        <span>MSIX Packaging</span>
        <p>Build modern Windows app packages alongside traditional MSI.</p>
        <span class="meta">Coming soon</span>
      </div>
      <div class="tutorial-card coming-soon">
        <span>Advanced MSI</span>
        <p>Custom actions, custom tables, merge modules, patches, and transforms.</p>
        <span class="meta">Coming soon</span>
      </div>
      <div class="tutorial-card coming-soon">
        <span>Localization</span>
        <p>Multi-language installers with culture fallback.</p>
        <span class="meta">Coming soon</span>
      </div>
      <div class="tutorial-card coming-soon">
        <span>Services</span>
        <p>Install, configure, and control Windows services.</p>
        <span class="meta">Coming soon</span>
      </div>
    </section>

    <section>
      <h2>Migration Guides</h2>
      <div class="tutorial-card">
        <a href="coming-from-wix.html">Coming from WiX</a>
        <p>Side-by-side translations of WiX XML patterns to FalkForge C#.</p>
        <span class="meta">15 min · For WiX users</span>
      </div>
    </section>
  </main>

  <script src="shared/tutorial.js"></script>
</body>
</html>
```

Add `.tutorial-card` styling to the CSS: bordered card with hover effect, `.coming-soon` has reduced opacity and no link.

**Step 2: Open in browser, verify layout and theme toggle**

**Step 3: Commit**

```
docs(tutorials): add tutorial hub index page
```

---

### Task 4: Create getting-started.html

**Files:**
- Create: `docs/tutorials/getting-started.html`
- Reference: `demo/01-hello-world/Program.cs` (read but do not modify)

**Step 1: Create the tutorial**

This wraps `demo/01-hello-world`. The page walks through every line of the demo's Program.cs, explaining each concept in plain English with collapsible deep-dives.

**Content structure:**

1. **Header metadata**: "Getting Started · 5 min · No prerequisites"

2. **What you'll build**: "By the end of this tutorial, you'll have a working MSI installer that places a file on the user's computer. Along the way, you'll learn what FalkForge is, how it works, and how to inspect the output."

3. **What is an MSI?** (collapsible): Brief explanation — MSI is the standard Windows Installer format. IT departments deploy them via Group Policy and SCCM. They support silent install, repair, and clean uninstall. FalkForge generates them from C# code.

4. **Prerequisites**: .NET 10 SDK installed. A text editor.

5. **Walk through `demo/01-hello-world/Program.cs`**: Break the 37-line file into logical chunks. For each chunk:
   - Show the code (with syntax highlighting spans)
   - Explain what it does in 1-2 sentences
   - Add a collapsible deep-dive for the underlying MSI concept

   Chunks to cover:
   - `using FalkForge` / `using FalkForge.Builders` — "These bring in the FalkForge API."
   - `Installer.Build(args, package => { ... })` — "This is the entry point. It compiles your description into an MSI file."
     - Deep-dive: How the compilation pipeline works
   - `package.Product(...)` — "Describes your product: name, version, manufacturer, upgrade code."
     - Deep-dive: What is an UpgradeCode and why it matters
   - `package.AddFile(...)` — "Adds a file to the installer."
     - Deep-dive: Components, key paths, and the Windows Installer file-tracking model
   - `.WithFeature("Complete")` — "Groups files into a feature the user can select."
     - Deep-dive: How MSI features and the feature tree work
   - `package.DialogTemplate(DialogTemplate.Minimal)` — "Sets the installer UI to a simple one-button dialog."
     - Deep-dive: The 5 built-in dialog templates and when to use each
   - `package.Localization(...)` / `package.Cabinet(...)` / `package.ReproducibleBuild()` — Brief one-liner each.
     - Deep-dive: What cabinets are, what reproducible builds mean

6. **Try it**:
   ```
   dotnet run --project demo/01-hello-world
   ```
   "This produces a `.msi` file in the output directory. You can double-click it to install, or inspect it:"
   ```
   forge inspect output/MyApp.msi
   ```

7. **What's next**: Three links:
   - "Continue learning → MSI Basics" (covers demos 02-05)
   - "Prefer JSON over C#? → JSON Configuration" (coming soon)
   - "Coming from WiX? → Migration Guide"

8. **Tutorial nav footer**: no Previous, Next → MSI Basics

**Writing rules for the subagent:**
- Short sentences. Active voice. Present tense.
- Define every term the first time: "A *component* is the smallest installable unit — one file plus its registry entries."
- No assumed MSI knowledge.
- Code snippets show the actual demo code. Reference file and line numbers.
- Use `<details class="deep-dive"><summary>...</summary>...</details>` for all deep-dives.
- Use syntax highlighting spans: `<span class="kw">using</span>`, `<span class="typ">Installer</span>`, `<span class="str">"MyApp"</span>`, `<span class="cmt">// comment</span>`, `<span class="method">Build</span>`, `<span class="num">1</span>`.

**Step 2: Open in browser, verify rendering**

**Step 3: Commit**

```
docs(tutorials): add Getting Started tutorial wrapping demo 01
```

---

### Task 5: Create msi-basics.html

**Files:**
- Create: `docs/tutorials/msi-basics.html`
- Reference: `demo/01-hello-world/Program.cs`, `demo/02-notepad-clone/Program.cs`, `demo/03-client-server/Program.cs`, `demo/04-dev-toolkit/Program.cs`, `demo/05-enterprise-suite/Program.cs` (read but do not modify)

**Step 1: Create the tutorial**

This tutorial covers the 5 core MSI building blocks by walking through demos 02-05 (demo 01 was covered in Getting Started). It teaches one concept per demo, building complexity.

**Content structure:**

1. **Header metadata**: "MSI Basics · 20 min · Prerequisite: Getting Started"

2. **What you'll learn**: "Shortcuts, file associations, registry, services, environment variables, features, and major upgrades — the tools you'll use in every real installer."

3. **Section: Shortcuts & File Associations (demo 02)**
   Walk through `demo/02-notepad-clone/Program.cs`. Key concepts:
   - `.AddShortcut()` with Desktop, StartMenu, Startup targets
   - `.AddRegistryEntry()` for application settings
   - `.RemoveRegistryOnUninstall()`
   - `.MajorUpgrade()` — what it does, why every real installer needs it
   - `.WithLicense()` — embedding a license agreement
   - Deep-dives: How Windows shortcuts work internally, Major upgrade vs. minor upgrade vs. patch

4. **Section: Services & Features (demo 03)**
   Walk through `demo/03-client-server/Program.cs`. Key concepts:
   - Multiple features (Client, Server, Documentation)
   - `.AddService()` — Windows service installation
   - `.AddLaunchCondition()` — blocking install if conditions aren't met
   - `.AddEnvironmentVariable()` — system-wide settings
   - `DialogTemplate.FeatureTree` — letting users choose components
   - Deep-dives: How Windows Services lifecycle works in MSI, Feature conditions

5. **Section: Registry & Environment Variables (demo 04)**
   Walk through `demo/04-dev-toolkit/Program.cs`. Key concepts:
   - Nested features (Editor > EditorPlugins)
   - `.AddFileAssociation()` — registering file types
   - PATH modification via environment variables
   - Custom actions with `.AddSetPropertyAction()`
   - `DialogTemplate.Mondo` — full-featured installer dialog
   - Deep-dives: How MSI environment variables differ from registry entries, What SetProperty custom actions do

6. **Section: Enterprise Feature Trees (demo 05)**
   Walk through `demo/05-enterprise-suite/Program.cs` — focus on the feature hierarchy, not every line. Key concepts:
   - Large feature trees with required/optional features
   - `.ServiceFailureActions()` — automatic service recovery
   - `.AddFont()` — font registration
   - `.AddCustomTable()` — storing custom data in the MSI
   - `DialogTemplate.Advanced` — per-user vs per-machine
   - Deep-dives: Feature tree design patterns (required root + optional children), Custom tables use cases

7. **Recap table**: Concept → Demo → API method. Quick reference.

8. **What's next**: Links to Custom UI, Bundles, Extensions tutorials.

9. **Tutorial nav footer**: Previous → Getting Started, Next → (coming soon)

**Same writing rules as Task 4.**

**Step 2: Open in browser, verify rendering**

**Step 3: Commit**

```
docs(tutorials): add MSI Basics tutorial wrapping demos 01-05
```

---

### Task 6: Create coming-from-wix.html

**Files:**
- Create: `docs/tutorials/coming-from-wix.html`
- Reference: Multiple demo Program.cs files for FalkForge examples

**Step 1: Create the migration guide**

Side-by-side translation format for WiX users. This is NOT a tutorial — it's a reference they consult while migrating.

**Content structure:**

1. **Header metadata**: "Coming from WiX · 15 min · For WiX v4/v5/v6 users"

2. **Intro**: "If you know WiX, you already know MSI. FalkForge targets the same Windows Installer tables — it just uses C# instead of XML. This guide translates the patterns you know."

3. **Concepts Mapping Table**:
   | WiX | FalkForge | Notes |
   |-----|-----------|-------|
   | `.wxs` source file | `Program.cs` | C# replaces XML |
   | `<Package>` | `PackageBuilder` | Fluent API |
   | `<Fragment>` | C# methods/classes | Just organize your code |
   | `<Component>` | Auto-generated | FalkForge creates components for you |
   | `<Feature>` | `.WithFeature()` | Chainable on any file/registry/service |
   | `<Directory>` | Auto-resolved | From file paths |
   | `WixUI_Mondo` | `DialogTemplate.Mondo` | Same 5 templates |
   | `<Property>` | `MsiProperty` | Type-safe with operators |
   | `<Condition>` | `Condition` class | `&`, `\|`, `!` operators |
   | `.wxl` localization | JSON localization | Culture fallback chain |
   | Burn engine | Bundle engine | NativeAOT, DPAPI, IMsiApi |
   | `wix build` | `forge build` or `dotnet run` | |
   | `wix msi decompile` | `forge decompile` | Also decompiles WiX Burn bundles |
   | Extension NuGet | Built-in extensions | No extra packages needed |

4. **Common Patterns (10 sections, each side-by-side)**:

   Use `.comparison` two-column grid. WiX XML on left (with XML syntax highlighting), FalkForge C# on right.

   Patterns to translate:
   a. **Adding files** — `<Component><File>` → `.AddFile()`
   b. **Registry entries** — `<RegistryValue>` → `.AddRegistryEntry()`
   c. **Windows services** — `<ServiceInstall>` → `.AddService()`
   d. **Shortcuts** — `<Shortcut>` → `.AddShortcut()`
   e. **Features** — `<Feature><ComponentRef>` → `.WithFeature()`
   f. **Custom actions** — `<CustomAction>` → `.AddCustomAction()`
   g. **Properties & conditions** — `<Property>`, `<Condition>` → `MsiProperty`, `Condition`
   h. **Major upgrade** — `<MajorUpgrade>` → `.MajorUpgrade()`
   i. **Localization** — `<WixLocalization>` → `.Localization(loc => ...)`
   j. **Launch conditions** — `<Launch>` → `.AddLaunchCondition()`

   Each pattern has a collapsible deep-dive explaining key differences (e.g., FalkForge auto-creates components, no ComponentGroup/ComponentRef indirection).

5. **Extension Mapping Table**:
   | WiX Extension | FalkForge Extension |
   |---------------|-------------------|
   | WixToolset.Firewall.wixext | Extensions.Firewall |
   | WixToolset.Iis.wixext | Extensions.Iis |
   | WixToolset.Sql.wixext | Extensions.Sql |
   | WixToolset.Netfx.wixext | Extensions.DotNet |
   | WixToolset.Util.wixext | Extensions.Util |
   | WixToolset.Dependency.wixext | Extensions.Dependency |
   | WixToolset.Http.wixext | Extensions.Http |
   | WixToolset.UI.wixext | DialogTemplate (built-in) |
   | WixToolset.Bal.wixext | InstallerApp (built-in WPF) |

6. **Bundle Migration** section:
   Key differences between Burn and FalkForge's bundle engine:
   - FalkForge engine is NativeAOT (no .NET runtime needed at install time)
   - Secure property passing via DPAPI (WiX passes as plaintext msiexec args)
   - MSI execution via IMsiApi P/Invoke (not msiexec.exe subprocess)
   - Built-in WPF UI framework (WiX requires writing a custom BA from scratch)
   - JSON update feeds (WiX uses Atom XML)

7. **What You Gain** section:
   Bullet list of concrete advantages — compile-time validation, MSIX support, SBOM generation, triple decompiler, NativeAOT engine, built-in WPF UI. Facts only, no marketing.

8. **Quick Start: Decompile Your Existing MSI**:
   ```
   forge decompile your-product.msi -o migrated.cs
   ```
   "This generates C# source code from your existing MSI. It's not a perfect migration — you'll need to clean up the output — but it gives you a working starting point."

9. **Tutorial nav footer**: No Previous/Next — this is a standalone reference.

**Writing rules:**
- Assume strong WiX knowledge. Don't explain what an MSI component is — they know.
- Focus on the translation, not the concepts.
- Be honest about differences — don't oversell.
- WiX XML examples should be realistic v4/v5 syntax (namespace `http://wixtoolset.org/schemas/v4/wxs`).

**Step 2: Open in browser, verify side-by-side layout works in both themes**

**Step 3: Commit**

```
docs(tutorials): add Coming from WiX migration guide
```

---

### Task 7: Final verification

**Step 1: Open index.html in browser**

Verify:
- Theme toggle works (persists across page navigation)
- All 3 tutorial links work
- "Coming soon" cards are visually distinct (lower opacity)
- Responsive layout works at 768px and below

**Step 2: Open each tutorial, verify:**

- Code blocks render with syntax highlighting
- Collapsible deep-dive sections open/close smoothly
- Copy button works on code blocks
- Side-by-side comparisons render correctly (coming-from-wix.html)
- Tutorial nav footer links work
- Dark and light themes both look good

**Step 3: Commit any fixes**

```
docs(tutorials): fix Phase 1 tutorial rendering issues
```

**Step 4: Final commit if everything is clean**

```
docs(tutorials): complete Phase 1 tutorial site
```
