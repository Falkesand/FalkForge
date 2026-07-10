using System.Diagnostics.CodeAnalysis;
using FalkForge.Signing;

namespace FalkForge.Cli;

/// <summary>
/// The build-time result of resolving a JSON <c>signing</c> section: the constructed
/// <see cref="ISignatureProvider"/> (C17 seam) plus any non-fatal security warnings to surface
/// (e.g. SignServer over http, unauthenticated NOAUTH mode). <see cref="None"/> represents
/// "signing absent or explicitly none" — <c>Result&lt;T&gt;</c> forbids null payloads, so absence
/// is modeled explicitly instead. The caller owns the provider and must dispose it after the
/// build when it is <see cref="IDisposable"/>.
/// </summary>
internal sealed record ResolvedSigning(ISignatureProvider? Provider, IReadOnlyList<string> Warnings)
{
    /// <summary>No signing configured — the build keeps its existing unsigned, synchronous path.</summary>
    public static ResolvedSigning None { get; } = new((ISignatureProvider?)null, []);

    /// <summary>True when a signature provider is configured and the build must sign a bundle.</summary>
    [MemberNotNullWhen(true, nameof(Provider))]
    public bool IsEnabled => Provider is not null;
}
