using System.Diagnostics.CodeAnalysis;
using FalkForge.Signing;

namespace FalkForge.Cli;

/// <summary>
/// The build-time result of resolving a JSON <c>signing</c> section: the constructed
/// <see cref="ISignatureProvider"/> (C17 seam) plus any non-fatal security warnings to surface
/// (e.g. SignServer over http, unauthenticated NOAUTH mode). <see cref="None"/> represents
/// "signing absent or explicitly none" — <c>Result&lt;T&gt;</c> forbids null payloads, so absence
/// is modeled explicitly instead. The caller owns the providers and must dispose them after the
/// build when they are <see cref="IDisposable"/>.
/// </summary>
/// <param name="Provider">The classical signature provider, or null when signing is off.</param>
/// <param name="Warnings">Non-fatal security warnings to surface before the build.</param>
/// <param name="PqProvider">
/// The ML-DSA companion provider for HYBRID signing (PQ-hybrid design §2.2), or null for
/// classical-only. Never present without <paramref name="Provider"/>: the loader requires a
/// classical key source for the pem provider, so a PQ-only config cannot be expressed.
/// </param>
internal sealed record ResolvedSigning(
    ISignatureProvider? Provider,
    IReadOnlyList<string> Warnings,
    ISignatureProvider? PqProvider = null)
{
    /// <summary>No signing configured — the build keeps its existing unsigned, synchronous path.</summary>
    public static ResolvedSigning None { get; } = new((ISignatureProvider?)null, []);

    /// <summary>True when a signature provider is configured and the build must sign a bundle.</summary>
    [MemberNotNullWhen(true, nameof(Provider))]
    public bool IsEnabled => Provider is not null;
}
