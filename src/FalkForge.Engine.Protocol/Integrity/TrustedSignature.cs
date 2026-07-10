namespace FalkForge.Engine.Protocol.Integrity;

/// <summary>
/// One valid, trusted, distinct signature collected by
/// <see cref="IntegrityEnvelopeCodec.CollectTrustedSignatures"/> (C19 §6.1): the accepted key's
/// fingerprint and the role(s) it holds, resolved from the pinned trusted set (never from the bundle).
/// The quorum evaluator (<see cref="QuorumEvaluator"/>) matches these against an operation's
/// <see cref="PolicyRule"/>.
/// </summary>
public readonly record struct TrustedSignature(string Fingerprint, TrustRole Roles);
