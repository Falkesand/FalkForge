using FalkForge.Engine.Protocol;

namespace FalkForge.Ui.ViewModels;

/// <summary>
/// One package's slice of the per-package feature picker: the feature tree for a single
/// feature-selectable MSI package, plus the wiring that pushes the current selection back to the
/// engine whenever a node is toggled. One section is shown per package that advertised features.
/// </summary>
public sealed class PackageFeatureSectionViewModel
{
    private readonly Action<string, IReadOnlyList<string>> _sendSelection;
    private readonly IReadOnlyList<PackageFeatureNodeViewModel> _allNodes;

    internal PackageFeatureSectionViewModel(
        string packageId,
        IReadOnlyList<MsiFeatureInfo> features,
        Action<string, IReadOnlyList<string>> sendSelection)
    {
        PackageId = packageId;
        _sendSelection = sendSelection;
        (Roots, _allNodes) = PackageFeatureTreeBuilder.Build(features, OnNodeToggled);
    }

    /// <summary>Manifest package id this section drives the selection for.</summary>
    public string PackageId { get; }

    /// <summary>Top-level feature nodes bound to the TreeView.</summary>
    public IReadOnlyList<PackageFeatureNodeViewModel> Roots { get; }

    /// <summary>The feature ids currently checked, in <c>Feature</c> table order.</summary>
    public IReadOnlyList<string> SelectedFeatureIds
    {
        get
        {
            var selected = new List<string>(_allNodes.Count);
            foreach (var node in _allNodes)
                if (node.IsChecked)
                    selected.Add(node.FeatureId);
            return selected;
        }
    }

    private void OnNodeToggled(PackageFeatureNodeViewModel _)
    {
        _sendSelection(PackageId, SelectedFeatureIds);
    }
}
