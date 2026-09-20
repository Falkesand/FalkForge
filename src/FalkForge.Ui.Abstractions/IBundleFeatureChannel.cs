namespace FalkForge.Ui.Abstractions;

/// <summary>
/// Optional capability for changing bundle-level feature selections after detection.
/// These choices select whole packages, independently of per-package MSI feature selection.
/// </summary>
public interface IBundleFeatureChannel
{
    /// <summary>
    /// Records a selection for a detected feature. The engine client sends all current
    /// selections, including unchanged defaults, before its next plan request.
    /// Required features remain selected. Unknown feature IDs throw ArgumentException.
    /// </summary>
    void SetFeatureSelection(string featureId, bool isSelected);
}
