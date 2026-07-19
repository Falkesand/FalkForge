using Xunit;

namespace FalkForge.Compiler.Bundle.Tests;

/// <summary>
/// Serializes every test class that mutates one of the real process environment variables
/// BundleCompiler's integrity/SBOM/elevation-companion pipeline consults (FALKFORGE_NO_SIGN,
/// FALKFORGE_GENERATE_SBOM, FALKFORGE_ELEVATION_COMPANION) against every other class in this
/// collection, so they never race within the same test-assembly process. See
/// <c>Compilation.BundleCompilerSigningTests</c> for the incident this collection fixed: a new
/// FALKFORGE_NO_SIGN-mutating test raced <c>Compilation.ManifestFieldPreservationTests</c> and
/// intermittently failed before this collection existed.
/// </summary>
[CollectionDefinition("BundleIntegrityEnv", DisableParallelization = true)]
public sealed class BundleIntegrityEnvCollection { }
