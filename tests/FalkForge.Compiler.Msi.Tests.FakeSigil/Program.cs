// Deterministic fake `sigil` CLI: a minimal test double for FalkForge.Compiler.Msi.Tests'
// MsiIntegritySigningTests. Real sigil is an external, user-installed tool; CI has no reason to
// have it on PATH, so the "sigil present but its subcommand fails -> the mandatory ECDSA
// signature still lands" contract (IntegritySigner.TryGenerateSbomAttestation, never-fatal by
// design) was previously only exercisable by accident on a dev machine that happens to have a
// real (but unconfigured) sigil install. This binary makes that path deterministic everywhere:
//
//   sigil --version   -> succeeds (so SigilDetector.IsAvailable() reports true), exit 0.
//   sigil <anything>  -> fails (simulating an unconfigured signing identity), exit 1.
if (args.Length > 0 && string.Equals(args[0], "--version", StringComparison.Ordinal))
{
    Console.WriteLine("fake-sigil 0.0.0-test");
    return 0;
}

Console.Error.WriteLine("fake-sigil: no signing identity configured (deterministic test double)");
return 1;
