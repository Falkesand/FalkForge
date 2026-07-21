namespace FalkForge.Engine.Protocol;

/// <summary>
/// A single row of a wrapped MSI's <c>Feature</c> table, read back for per-package
/// feature selection. Distinct from <see cref="FeatureState"/> (which models the
/// bundle-level, whole-package feature system); this describes the features *inside*
/// one MSI package so the UI can drive an <c>ADDLOCAL</c> selection for that package.
/// </summary>
/// <param name="FeatureId">The MSI <c>Feature</c> primary-key column.</param>
/// <param name="Title">Localized display title, or null when the column is null.</param>
/// <param name="Description">Localized description, or null when the column is null.</param>
/// <param name="Parent">The <c>Feature_Parent</c> foreign key, or null for a root feature.</param>
/// <param name="Level">The MSI <c>Level</c> column (install level).</param>
/// <param name="Display">The MSI <c>Display</c> column (UI ordering / visibility).</param>
/// <param name="EstimatedSize">
/// Estimated installed size in bytes. Currently always 0 — the FeatureComponents/File
/// size join is deferred until a later stage that needs it.
/// </param>
public readonly record struct MsiFeatureInfo(
    string FeatureId,
    string? Title,
    string? Description,
    string? Parent,
    int Level,
    int Display,
    long EstimatedSize);
