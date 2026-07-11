namespace FalkForge.Extensions.Dependency;

/// <summary>
///     One planned MSI-time dependency version check. The provider's registered version is read
///     from HKLM into <see cref="PropertyName"/> by an AppSearch/RegLocator pair; an immediate
///     JScript custom action (<see cref="EvalActionId"/>, script bytes in
///     <see cref="ScriptBytes"/> under Binary key <see cref="BinaryName"/>) performs a real
///     component-wise numeric version comparison — matching <c>VersionRange.IsSatisfiedBy</c>
///     semantics, which a static MSI condition string cannot do because MSI condition operators
///     compare lexicographically, not by version — and sets <see cref="FailPropertyName"/> when
///     the requirement is unsatisfied. A Type 19 custom action (<see cref="AbortActionId"/>)
///     then aborts the install with <see cref="Message"/>, conditioned on
///     <see cref="FailPropertyName"/>. Both actions are sequenced early (after AppSearch, before
///     InstallInitialize) in the install sequences. Produced by
///     <see cref="DependencyVersionCheckPlanner"/>.
/// </summary>
internal sealed record DependencyVersionCheck(
    string PropertyName,
    string FailPropertyName,
    string SignatureName,
    string RegistryKeyPath,
    string BinaryName,
    byte[] ScriptBytes,
    string EvalActionId,
    int EvalSequence,
    string AbortActionId,
    int AbortSequence,
    string Message);
