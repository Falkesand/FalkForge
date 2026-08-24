namespace FalkForge.Engine.Planning;

/// <summary>
/// One MSI transform (.mst) an author declared for a package, resolved to the absolute path of its
/// extracted, signed payload on the TARGET machine. <see cref="FalkForge.Engine.Pipeline.ApplyStep"/>
/// resolves each declared transform id under the bootstrapper-forwarded payload root (with the same
/// containment guard the MSI itself uses) and records the pairs on
/// <see cref="PlanAction.ResolvedTransformPaths"/>. The elevated companion binds each path to the
/// publisher-SIGNED hash and the SIGNED package-to-transform association before applying it, so a
/// same-user attacker cannot substitute or re-target a transform on the SYSTEM install.
/// </summary>
/// <param name="Id">The author-chosen transform id; matches the signed payload entry's name.</param>
/// <param name="Path">The absolute, containment-checked path to the extracted transform (.mst).</param>
public readonly record struct ResolvedTransform(string Id, string Path);
