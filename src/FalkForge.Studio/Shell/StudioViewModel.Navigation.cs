using System.Linq;
using FalkForge.Studio.Editors.BuildSettingsEditor;
using FalkForge.Studio.Editors.BundleChain;
using FalkForge.Studio.Editors.BundlePackagesEditor;
using FalkForge.Studio.Editors.BundleSettingsEditor;
using FalkForge.Studio.Editors.CustomActionsEditor;
using FalkForge.Studio.Editors.DependencyGraph;
using FalkForge.Studio.Editors.DialogEditor;
using FalkForge.Studio.Editors.DiffViewer;
using FalkForge.Studio.Editors.EnvironmentEditor;
using FalkForge.Studio.Editors.FeaturesEditor;
using FalkForge.Studio.Editors.FilesEditor;
using FalkForge.Studio.Editors.FirewallEditor;
using FalkForge.Studio.Editors.OdbcEditor;
using FalkForge.Studio.Editors.PerfCountersEditor;
using FalkForge.Studio.Editors.ProductEditor;
using FalkForge.Studio.Editors.RegistryEditor;
using FalkForge.Studio.Editors.ScheduledTasksEditor;
using FalkForge.Studio.Editors.ServicesEditor;
using FalkForge.Studio.Editors.ShortcutsEditor;
using FalkForge.Studio.Editors.SqlEditor;
using FalkForge.Studio.Editors.TableInspector;
using FalkForge.Studio.Editors.UiEditor;
using FalkForge.Studio.Editors.XmlConfigEditor;
using FalkForge.Studio.Navigation;

namespace FalkForge.Studio.Shell;

public sealed partial class StudioViewModel
{
    /// <summary>
    /// Activates the editor associated with <paramref name="nodeKey"/>, creating it on first use.
    /// </summary>
    public void NavigateTo(string nodeKey)
    {
        if (!_editors.TryGetValue(nodeKey, out var editor))
        {
            editor = CreateEditor(nodeKey);
            if (editor is not null)
                _editors[nodeKey] = editor;
        }
        CurrentEditor = editor;
    }

    private ViewModelBase? CreateEditor(string nodeKey) => nodeKey switch
    {
        "product"        => CreateProductEditor(),
        "files"          => new FilesEditorViewModel(_project),
        "features"       => new FeaturesEditorViewModel(_project),
        "registry"       => new RegistryEditorViewModel(_project),
        "services"       => new ServicesEditorViewModel(_project),
        "shortcuts"      => new ShortcutsEditorViewModel(_project),
        "environment"    => new EnvironmentEditorViewModel(_project),
        "customActions"  => new CustomActionsEditorViewModel(_project),
        "sql"            => new SqlEditorViewModel(_project),
        "firewall"       => new FirewallEditorViewModel(_project),
        "xmlConfig"      => new XmlConfigEditorViewModel(_project),
        "scheduledTasks" => new ScheduledTasksEditorViewModel(_project),
        "perfCounters"   => new PerfCountersEditorViewModel(_project),
        "odbc"           => new OdbcEditorViewModel(_project),
        "ui"             => new UiEditorViewModel(_project.Ui),
        "dialogs"        => new DialogEditorViewModel(_project.Ui),
        "build"          => new BuildSettingsEditorViewModel(_project.Build),
        "bundleSettings" => new BundleSettingsEditorViewModel(_project.BundleSettings ??= new()),
        "bundlePackages" => new BundlePackagesEditorViewModel(_project),
        "bundleChain"    => new BundleChainViewModel(_project),
        "diffViewer"     => new DiffViewerViewModel(_project),
        "tableInspector" => new TableInspectorViewModel(),
        "dependencyGraph" => new DependencyGraphViewModel(_project),
        _                => null
    };

    private ProductEditorViewModel CreateProductEditor()
    {
        var vm = new ProductEditorViewModel(_project.Product, _project);
        vm.ProjectTypeChanged += OnProjectTypeChanged;
        return vm;
    }

    private void OnProjectTypeChanged(object? sender, EventArgs e)
    {
        var currentKey = _editors.FirstOrDefault(kv => kv.Value == CurrentEditor).Key;
        TreeNodes.Clear();
        BuildDefaultTree();

        if (currentKey is not null && _editors.TryGetValue(currentKey, out var editor) && TreeNodeExists(currentKey))
        {
            CurrentEditor = editor;
        }
        else if (currentKey != "product")
        {
            // Current editor's node was removed; fall back to product.
            NavigateTo("product");
        }
    }

    private bool TreeNodeExists(string key)
        => TreeNodes.Any(n => n.NodeKey == key || n.Children.Any(c => c.NodeKey == key));

    private void RefreshCurrentEditor()
    {
        var currentKey = _editors.FirstOrDefault(kv => kv.Value == CurrentEditor).Key;
        _editors.Clear();
        if (currentKey is not null)
            NavigateTo(currentKey);
        else
            CurrentEditor = null;
    }

    // ── Undo / Redo ───────────────────────────────────────────────────────────

    private void Undo()
    {
        var restored = _undoManager.Undo(_project);
        if (restored is null) return;

        _project = restored;
        RefreshCurrentEditor();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private void Redo()
    {
        var restored = _undoManager.Redo(_project);
        if (restored is null) return;

        _project = restored;
        RefreshCurrentEditor();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }
}
