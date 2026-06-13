namespace FalkForge.Decompiler;

/// <summary>
/// Options controlling how <see cref="MigrationProjectGenerator"/> produces a migration project.
/// </summary>
/// <param name="FalkForgeSourcePath">
/// Relative or absolute path to the FalkForge <c>src/</c> directory whose
/// <c>.csproj</c> files the generated project will reference via
/// <c>&lt;ProjectReference&gt;</c>.  Forward slashes are used in emitted XML.
/// </param>
/// <param name="ProjectName">
/// The name of the generated project (used as the <c>.csproj</c> filename stem
/// and as the project's identity within any containing solution).
/// </param>
public sealed record MigrationOptions(string FalkForgeSourcePath, string ProjectName);
