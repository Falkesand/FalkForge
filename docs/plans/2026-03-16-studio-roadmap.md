# FalkForge Studio Roadmap

> **Target audience:** Studio should be a full alternative to the code-first C# API — every capability available via the fluent API should eventually be reachable through Studio.

> **Competitive reference:** Advanced Installer (product details tabs, 75+ themes, dialog editor, repackager, CI/CD integration). See their [feature list](https://www.advancedinstaller.com/feats-list.html) and [gallery](https://www.advancedinstaller.com/gallery.html).

---

## Phase 1: Editor Enrichment (Foundation)

**Goal:** Make every editor self-sufficient — users should never need to leave Studio to figure out what a field does or find a missing option.

### 1.1 Product Editor — expand to match industry standard

- Add tabbed layout: General | Support Info | Programs & Features
- **General tab** (current fields — already done)
- **Support Info tab**: Support URL, Update Info URL, Help URL, Phone, Email, Comments — maps to standard MSI properties (`ARPHELPLINK`, `ARPURLINFOABOUT`, `ARPHELPTELEPHONE`, `ARPCONTACT`, `ARPCOMMENTS`)
- **Programs & Features tab**: Custom icon picker (browse for .ico), Disable Modify/Repair/Remove checkboxes, "Do not show in list" checkbox — maps to `ARPNOMODIFY`, `ARPNOREPAIR`, `ARPNOREMOVE`, `ARPSYSTEMCOMPONENT`

### 1.2 Feature Editor — install levels and display control

- Add Install Level dropdown: Typical (1), Custom (3), Complete (4), Disabled (0)
- Feature tree display mode: Expanded / Collapsed / Hidden
- Per-feature icon (optional .ico path)
- Drag-and-drop reordering in the feature tree

### 1.3 Files Editor — richer file management

- Browse button for source path (folder picker dialog)
- Drag-and-drop files from Windows Explorer
- File properties panel: read-only, hidden, system, vital flags
- Wildcard support: add `*.dll` patterns instead of individual files

### 1.4 License/EULA integration

- Move license file selection from UI Editor into Product Editor (Support Info tab or dedicated tab)
- Preview panel showing RTF content inline

---

## Phase 2: Workflow & Productivity

**Goal:** Make Studio efficient for day-to-day use — reduce clicks, add validation feedback, and support the full build-test-iterate cycle.

### 2.1 Live Validation Panel

- Persistent bottom panel showing validation warnings/errors as you edit
- Real-time — runs validators on every change (debounced 500ms)
- Clickable errors that navigate to the offending editor/field
- Severity levels: Error (blocks build), Warning (informational), Info (best practice suggestions)
- Maps to existing Core validators (PKG001-011, FEA001-005, SVC001-008, etc.)

### 2.2 Build Output & Progress

- Replace current text-only output with structured build panel
- Progress bar during compilation
- Error/warning summary with clickable file paths
- "Build succeeded" / "Build failed" status bar indicator
- One-click "Build & Test Install" (compile + launch the MSI silently for smoke testing)

### 2.3 Project Templates

- New Project wizard with templates: Minimal, Desktop App, Windows Service, Web Application, Enterprise Suite
- Each template pre-populates sensible defaults (features, files, registry, services as appropriate)
- Template descriptions with what's included

### 2.4 Undo/Redo

- Global undo/redo stack across all editors
- Ctrl+Z / Ctrl+Y keyboard shortcuts
- Undo history panel (optional, low priority)

### 2.5 Import/Export

- Import from WiX XML (.wxs) → StudioProject
- Import from existing MSI (uses Decompiler → StudioProject)
- Export to C# script (.csx) for users who outgrow Studio

---

## Phase 3: Advanced Capabilities

**Goal:** Differentiate Studio from competitors and support enterprise workflows.

### 3.1 Visual Dialog Editor

- WYSIWYG editor for installer dialogs (drag controls, set properties)
- Preview across all 5 built-in dialog templates (Minimal, InstallDir, FeatureTree, Mondo, Advanced)
- Custom dialog support — add new dialogs to the sequence
- Theme picker with live preview (banner images, watermark, colors)

### 3.2 Dependency Graph Visualization

- Interactive graph showing: Features → Components → Files relationships
- Highlight orphaned files, circular dependencies, missing references
- Click-to-navigate from graph nodes to editors
- Useful for auditing complex installers

### 3.3 Bundle Orchestration View

- Visual chain editor for EXE bundles — drag/reorder packages
- Rollback boundary markers (visual dividers)
- Package dependency arrows showing install order
- Prerequisite flow visualization (what downloads when)

### 3.4 Diff & History

- Side-by-side diff of StudioProject JSON between saves
- Integration with git — show what changed since last commit
- "Compare with..." to diff two project files

### 3.5 CI/CD Export

- Generate GitHub Actions / Azure DevOps / Jenkins pipeline YAML from project
- One-click "Set up CI" that creates the pipeline file
- Build configuration profiles (Debug with no signing, Release with signing + timestamp)

### 3.6 MSI Table Inspector

- Read-only view of compiled MSI database tables (like Advanced Installer's Direct Editor)
- Uses existing Decompiler infrastructure
- Useful for debugging — "what did the compiler actually emit?"

---

## Current Status (2026-03-16)

| Area | Status |
|------|--------|
| 18 editor views with DataTemplates | Done |
| Tooltips + descriptions on all fields | Done |
| Output Format selector (MSI/Bundle/MSIX) | Done |
| CompileMsi | Done |
| CompileMsix | Done |
| CompileBundle | Done |
| 141 tests passing | Done |
