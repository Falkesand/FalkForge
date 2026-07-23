namespace FalkForge.Extensions.DotNet;

/// <summary>
///     Fully-resolved, MSI-native detection plan for one authored <see cref="DotNetCoreSearchModel"/>.
///     Computed once by <see cref="DotNetSearchPlanner"/> and shared across the
///     <c>Signature</c>/<c>DrLocator</c>/<c>AppSearch</c>/<c>LaunchCondition</c> contributors so every
///     table row for a given search agrees on the same identifiers.
/// </summary>
/// <param name="PropertyName">
///     The MSI property (and <c>AppSearch</c>/<c>LaunchCondition</c> condition) name — the author's own
///     <see cref="DotNetCoreSearchModel.VariableName"/>, used verbatim so the C# and JSON authoring paths
///     name the same property the rest of the package (e.g. <c>package.Require</c>) reads.
/// </param>
/// <param name="SignatureName">Content-hash-salted <c>Signature</c> table key; collision-free across multiple searches.</param>
/// <param name="Path">
///     The <c>DrLocator.Path</c> value: an MSI Formatted-text folder reference (e.g.
///     <c>[ProgramFiles64Folder]dotnet\shared\Microsoft.NETCore.App</c>) that resolves to the shared
///     framework's directory. Versions live one level below as subdirectories, which is why the search
///     depth is fixed at 1.
/// </param>
/// <param name="FileName">
///     The sentinel file whose on-disk version is compared against <see cref="MinVersion"/>. Chosen per
///     runtime type — swappable here if a future .NET release changes a shared framework's binary layout.
/// </param>
/// <param name="MinVersion">Four-part, zero-filled minimum version string for the <c>Signature.MinVersion</c> file-version search.</param>
/// <param name="Message">
///     Optional <c>LaunchCondition.Description</c>. Null when the author gates presence themselves via
///     <c>package.Require(...)</c> (the C# authoring path) rather than asking the extension to emit its
///     own launch condition (the JSON authoring path).
/// </param>
internal sealed record DotNetSearchPlan(
    string PropertyName,
    string SignatureName,
    string Path,
    string FileName,
    string MinVersion,
    string? Message);
