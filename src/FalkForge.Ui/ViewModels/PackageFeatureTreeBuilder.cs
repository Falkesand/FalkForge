using FalkForge.Engine.Protocol;

namespace FalkForge.Ui.ViewModels;

/// <summary>
/// Builds a parent→child tree of <see cref="PackageFeatureNodeViewModel"/> from a flat list of
/// MSI <c>Feature</c> rows, honouring the MSI visibility columns:
/// <list type="bullet">
/// <item><description><c>Level == 0</c> — the feature is absent/disabled; excluded entirely.</description></item>
/// <item><description><c>Display == 0</c> — the feature is hidden from the UI; excluded from the tree.</description></item>
/// </list>
/// A visible feature whose <c>Feature_Parent</c> chain leads only through excluded/absent parents is
/// re-parented to the nearest visible ancestor, or promoted to a root when none exists, so no visible
/// feature is orphaned by a hidden parent.
/// </summary>
internal static class PackageFeatureTreeBuilder
{
    /// <summary>
    /// Constructs the visible feature tree. Every produced node starts checked (selected for install
    /// by default). <paramref name="onToggled"/> is wired to each node so a checkbox change bubbles
    /// back to the owning section.
    /// </summary>
    /// <returns>
    /// The top-level roots (for the TreeView) and a flat list of every node in the tree (for
    /// collecting the current selection), both in original <c>Feature</c> table order.
    /// </returns>
    public static (IReadOnlyList<PackageFeatureNodeViewModel> Roots, IReadOnlyList<PackageFeatureNodeViewModel> All)
        Build(IReadOnlyList<MsiFeatureInfo> features, Action<PackageFeatureNodeViewModel> onToggled)
    {
        // Map ALL rows by id (last wins) so ancestry can be walked THROUGH excluded parents.
        var byId = new Dictionary<string, MsiFeatureInfo>(features.Count, StringComparer.Ordinal);
        foreach (var f in features)
            byId[f.FeatureId] = f;

        // Visible = installable (Level != 0) AND shown (Display != 0), in Feature table order.
        var visible = new List<MsiFeatureInfo>(features.Count);
        var visibleIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in features)
        {
            if (f.Level == 0 || f.Display == 0)
                continue;
            visible.Add(f);
            visibleIds.Add(f.FeatureId);
        }

        var nodes = new Dictionary<string, PackageFeatureNodeViewModel>(visible.Count, StringComparer.Ordinal);
        // Each node's Children is backed by a mutable list captured here and filled in the second pass.
        var childLists = new Dictionary<string, List<PackageFeatureNodeViewModel>>(visible.Count, StringComparer.Ordinal);
        var all = new List<PackageFeatureNodeViewModel>(visible.Count);

        foreach (var f in visible)
        {
            var children = new List<PackageFeatureNodeViewModel>();
            childLists[f.FeatureId] = children;
            var node = new PackageFeatureNodeViewModel(
                f.FeatureId,
                string.IsNullOrEmpty(f.Title) ? f.FeatureId : f.Title,
                f.Description,
                children,
                isChecked: true,
                onToggled);
            nodes[f.FeatureId] = node;
            all.Add(node);
        }

        var roots = new List<PackageFeatureNodeViewModel>();
        foreach (var f in visible)
        {
            var parentId = NearestVisibleAncestor(f.Parent, byId, visibleIds, features.Count);
            if (parentId is not null && childLists.TryGetValue(parentId, out var bucket))
                bucket.Add(nodes[f.FeatureId]);
            else
                roots.Add(nodes[f.FeatureId]);
        }

        return (roots, all);
    }

    /// <summary>
    /// Walks the <c>Feature_Parent</c> chain from <paramref name="parent"/> up to the first visible
    /// ancestor. Returns null when the chain reaches a root or dead-ends only through excluded rows.
    /// The <paramref name="guardLimit"/> bounds the walk so a malformed cyclic parent chain cannot spin.
    /// </summary>
    private static string? NearestVisibleAncestor(
        string? parent,
        IReadOnlyDictionary<string, MsiFeatureInfo> byId,
        HashSet<string> visibleIds,
        int guardLimit)
    {
        var cur = parent;
        var guard = 0;
        while (cur is not null && guard++ <= guardLimit)
        {
            if (visibleIds.Contains(cur))
                return cur;
            cur = byId.TryGetValue(cur, out var pf) ? pf.Parent : null;
        }

        return null;
    }
}
