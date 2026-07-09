using System.IO;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Shell;

public sealed partial class StudioViewModel
{
    // ── Project lifecycle — New ───────────────────────────────────────────────

    /// <summary>Resets to a blank new project.</summary>
    public void NewProject()
    {
        _project = StudioProjectLoader.NewProject();
        _editors.Clear();
        CurrentEditor = null;
        TreeNodes.Clear();
        BuildDefaultTree();
        OutputText = "New project created.";
        Title = "FalkForge Studio - Untitled";
        _projectPath = null;
        _undoManager.Clear();
        _undoManager.SaveState(_project);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    /// <summary>Resets to a new project created from <paramref name="template"/>.</summary>
    public void NewProject(ProjectTemplate template)
    {
        _project = template.Create();
        _projectPath = null;
        Title = $"FalkForge Studio - {_project.Product.Name}";
        _editors.Clear();
        CurrentEditor = null;
        TreeNodes.Clear();
        BuildDefaultTree();
        OutputText = $"New project created from template: {template.Name}";
        _undoManager.Clear();
        _undoManager.SaveState(_project);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    // ── Project lifecycle — Load ──────────────────────────────────────────────

    /// <summary>Loads a saved project from disk.</summary>
    public void LoadProject(string path)
    {
        _project = StudioProjectLoader.LoadFromFile(path);
        _editors.Clear();
        CurrentEditor = null;
        TreeNodes.Clear();
        BuildDefaultTree();
        OutputText = $"Opened: {path}";
        Title = $"FalkForge Studio - {Path.GetFileName(path)}";
        _projectPath = path;
        _baseDirectory = Path.GetDirectoryName(path) ?? ".";
        _undoManager.Clear();
        _undoManager.SaveState(_project);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    // ── Project lifecycle — Import ────────────────────────────────────────────

    /// <summary>Imports an existing MSI file and replaces the current project.</summary>
    public void ImportMsi(string msiPath)
    {
        var result = Import.MsiImporter.Import(msiPath);
        if (result.IsFailure)
        {
            OutputText = $"Import failed: {result.Error.Message}";
            return;
        }

        _project = result.Value;
        _editors.Clear();
        CurrentEditor = null;
        TreeNodes.Clear();
        BuildDefaultTree();
        OutputText = $"Imported MSI: {Path.GetFileName(msiPath)}";
        Title = $"FalkForge Studio - {Path.GetFileName(msiPath)} (imported)";
        _projectPath = null;
        _undoManager.Clear();
        _undoManager.SaveState(_project);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    /// <summary>Imports a WiX source file and replaces the current project.</summary>
    public void ImportWix(string wxsPath)
    {
        var result = Import.WixImporter.Import(wxsPath);
        if (result.IsFailure)
        {
            OutputText = $"Import failed: {result.Error.Message}";
            return;
        }

        _project = result.Value;
        _editors.Clear();
        CurrentEditor = null;
        TreeNodes.Clear();
        BuildDefaultTree();
        OutputText = $"Imported WiX source: {Path.GetFileName(wxsPath)}";
        Title = $"FalkForge Studio - {Path.GetFileName(wxsPath)} (imported)";
        _projectPath = null;
        _undoManager.Clear();
        _undoManager.SaveState(_project);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    // ── Project lifecycle — Save / Export ─────────────────────────────────────

    /// <summary>Saves the project to <paramref name="path"/> (defaults to last saved path).</summary>
    public void SaveProject(string? path = null)
    {
        path ??= _projectPath;
        if (path is null) return;
        StudioProjectLoader.SaveToFile(_project, path);
        OutputText = $"Saved: {path}";
        Title = $"FalkForge Studio - {Path.GetFileName(path)}";
        _projectPath = path;
    }

    /// <summary>Exports the current project as a C# script.</summary>
    public Result<string> ExportCSharpScript()
        => Export.CSharpExporter.Export(_project);

    /// <summary>The underlying project model (for testing and export).</summary>
    public StudioProject Project => _project;
}
