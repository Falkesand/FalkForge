// Deterministic fake `sigil` CLI: a minimal test double for FalkForge.Compiler.Msi.Tests'
// MsiIntegritySigningTests. Real sigil is an external, user-installed tool; CI has no reason to
// have it on PATH, so the "sigil present but its subcommand fails -> the mandatory ECDSA
// signature still lands" contract (IntegritySigner.TryGenerateSbomAttestation, never-fatal by
// design) was previously only exercisable by accident on a dev machine that happens to have a
// real (but unconfigured) sigil install. This binary makes that path deterministic everywhere:
//
//   sigil --version   -> succeeds (so SigilDetector.IsAvailable() reports true), exit 0.
//   sigil <anything>  -> fails (simulating an unconfigured signing identity), exit 1.
//
// One opt-in exception, gated behind the FAKESIGIL_ATTEST_SUCCEEDS environment variable so the
// default everything-fails double above is completely unchanged for the tests that depend on it:
//
//   FAKESIGIL_ATTEST_SUCCEEDS=1 sigil attest ... -> succeeds, writing a DSSE-shaped envelope that
//   carries the --predicate document (the SBOM) verbatim as its payload, exit 0.
//
// That success mode exists so IntegrityAttestationSbomToctouTests can read back exactly which
// digests the compiler asked to have attested — without it, IntegritySigner swallows the failed
// attest and no attestation is ever observable.
if (args.Length > 0 && string.Equals(args[0], "--version", StringComparison.Ordinal))
{
    Console.WriteLine("fake-sigil 0.0.0-test");
    return 0;
}

if (args.Length > 0
    && string.Equals(args[0], "attest", StringComparison.Ordinal)
    && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FAKESIGIL_ATTEST_SUCCEEDS")))
{
    var predicatePath = OptionValue(args, "--predicate");
    var outputPath = OptionValue(args, "--output");

    if (predicatePath is null || outputPath is null || !File.Exists(predicatePath))
    {
        Console.Error.WriteLine(
            "fake-sigil: attest requires --predicate <existing file> and --output <path>.");
        return 1;
    }

    // The predicate is a JSON document, so embedding it verbatim keeps the envelope valid JSON
    // and lets a test assert on the exact SBOM the compiler produced.
    //
    // NOT CONFORMANT DSSE, deliberately. A real DSSE envelope's "payload" is a BASE64 string and its
    // signature is computed over PAE(payloadType, payload); this writes the predicate as a raw nested
    // JSON object and a constant fake "sig". That is fine for the one thing this double is for —
    // letting a test read back which digests the compiler asked to have attested — because nothing in
    // FalkForge parses this output; IntegritySigner copies the file through verbatim. Do NOT build
    // DSSE-parsing, base64-decoding, or signature-verifying tests on top of this shape: they would be
    // testing the double, not the format. Emit real DSSE here first if that is ever needed.
    var predicateJson = File.ReadAllText(predicatePath);
    File.WriteAllText(
        outputPath,
        "{\"payloadType\":\"application/vnd.in-toto+json\",\"payload\":" + predicateJson +
        ",\"signatures\":[{\"sig\":\"fake-sigil\"}]}");
    return 0;
}

Console.Error.WriteLine("fake-sigil: no signing identity configured (deterministic test double)");
return 1;

static string? OptionValue(string[] arguments, string name)
{
    var index = Array.IndexOf(arguments, name);
    return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
}
