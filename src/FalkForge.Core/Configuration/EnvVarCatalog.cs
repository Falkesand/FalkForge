using System.Globalization;

namespace FalkForge.Configuration;

/// <summary>
/// Central, discoverable catalog of every environment variable FalkForge itself defines and
/// reads. Before this catalog existed, 23 production call sites across 10 files each called
/// <see cref="Environment.GetEnvironmentVariable(string)"/> directly with an inline string
/// literal for the name, so there was no single place to see the full set of supported
/// variables, and no shared validation for the ones that parse to a non-string type.
///
/// <para><b>Scope rule — what belongs here.</b> A variable gets a name constant in this catalog
/// when FalkForge itself defines the name as part of its configuration surface, regardless of
/// how many assemblies read it (discoverability wins over "used in only one place"). Two
/// categories are deliberately excluded:</para>
/// <list type="bullet">
///   <item><description><b>Dynamically-named variables.</b> <c>SigningProviderFactory.RequireEnv</c>
///   reads an environment variable whose NAME is itself a user-supplied config value (e.g.
///   <c>signing.keyEnv</c> in the JSON config) — there is no fixed name to catalog. Those call
///   sites still route through <see cref="GetRaw"/> (the shared read primitive) but do not get a
///   named constant here.</description></item>
///   <item><description><b>OS/platform-owned variables.</b> <c>SESSIONNAME</c> (read in
///   <c>BuiltInVariables.PopulateSessionInfo</c> for Remote Desktop detection) is a Windows
///   session variable, not something FalkForge defines — it is read through the existing
///   testable <c>IEnvironment</c>/<c>IPlatformServices</c> abstraction and is out of scope
///   here.</description></item>
/// </list>
///
/// <para><b>Typed-accessor rule.</b> A variable gets a bespoke typed accessor (beyond
/// <see cref="GetRaw"/>/<see cref="SetRaw"/>) only when its value needs real type coercion that
/// is shared by more than one call site: <see cref="TryGetSourceDateEpoch"/> centralizes the
/// Unix-timestamp parse (reused by 4 call sites with 3 different fallback policies — the parse
/// is shared, the policy on malformed/absent is NOT, so callers still decide what to do with a
/// <see cref="Result{T}"/>), and <see cref="IsSigningDisabled"/>/<see cref="IsSbomGenerationRequested"/>
/// centralize the "presence, not parsing" opt-in-flag idiom (any non-empty value — including
/// <c>"0"</c> or <c>"false"</c> — means "on"; only absence or empty means "off"). Variables whose
/// validation is inherently call-site-specific (file-existence checks, certificate loading,
/// directory probing) stay on the plain <see cref="GetRaw"/> primitive plus a name constant here
/// for discoverability — bespoke wrapper methods for those would just rename
/// <see cref="Environment.GetEnvironmentVariable(string)"/> without adding behavior.</para>
/// </summary>
public static class EnvVarCatalog
{
    // ── Names ────────────────────────────────────────────────────────────

    /// <summary>Unix timestamp that pins a reproducible build's timestamps and derived IDs.</summary>
    public const string SourceDateEpoch = "SOURCE_DATE_EPOCH";

    /// <summary>Opt-in flag (presence-only): generate a CycloneDX SBOM sidecar alongside the compiled output.</summary>
    public const string GenerateSbom = "FALKFORGE_GENERATE_SBOM";

    /// <summary>Opt-in flag (presence-only): skip ECDSA payload/manifest integrity signing.</summary>
    public const string NoSign = "FALKFORGE_NO_SIGN";

    /// <summary>Path to the published NativeAOT engine executable, or a directory containing it.</summary>
    public const string EngineStub = "FALKFORGE_ENGINE_STUB";

    /// <summary>Path to the published elevation companion executable, or a directory containing it.</summary>
    public const string ElevationCompanion = "FALKFORGE_ELEVATION_COMPANION";

    /// <summary>Base URL of the SignServer instance used for remote signing.</summary>
    public const string SignServerUrl = "SIGNSERVER_URL";

    /// <summary>SignServer PlainSigner worker name or numeric id.</summary>
    public const string SignServerWorker = "SIGNSERVER_WORKER";

    /// <summary>SignServer auth mode: <c>none|clientcert|basic|bearer</c>.</summary>
    public const string SignServerAuth = "SIGNSERVER_AUTH";

    /// <summary>Bearer token used when <see cref="SignServerAuth"/> is <c>bearer</c>.</summary>
    public const string SignServerBearerToken = "SIGNSERVER_BEARER_TOKEN";

    /// <summary>Basic-auth username used when <see cref="SignServerAuth"/> is <c>basic</c>.</summary>
    public const string SignServerBasicUser = "SIGNSERVER_BASIC_USER";

    /// <summary>Basic-auth password used when <see cref="SignServerAuth"/> is <c>basic</c>.</summary>
    public const string SignServerBasicPass = "SIGNSERVER_BASIC_PASS";

    /// <summary>PFX client-certificate path used when <see cref="SignServerAuth"/> is <c>clientcert</c>.</summary>
    public const string SignServerClientCert = "SIGNSERVER_CLIENT_CERT";

    /// <summary>Password for <see cref="SignServerClientCert"/>.</summary>
    public const string SignServerClientCertPassword = "SIGNSERVER_CLIENT_CERT_PASSWORD";

    /// <summary>Operator-facing key label copied verbatim into the signature envelope's <c>keyId</c>.</summary>
    public const string SignServerKeyId = "SIGNSERVER_KEY_ID";

    // ── Generic primitive ────────────────────────────────────────────────

    /// <summary>
    /// The single read primitive every environment-variable access in FalkForge should route
    /// through — including dynamically-named lookups (a config-supplied env var name) that have
    /// no fixed catalog entry. A thin wrapper today; the seam exists so process-env mutation in
    /// tests (<c>Environment.SetEnvironmentVariable</c>) keeps working unchanged — no caching.
    /// </summary>
    public static string? GetRaw(string name) => Environment.GetEnvironmentVariable(name);

    /// <summary>The matching write primitive. <paramref name="value"/> of <c>null</c> clears the variable.</summary>
    public static void SetRaw(string name, string? value) => Environment.SetEnvironmentVariable(name, value);

    // ── SOURCE_DATE_EPOCH ────────────────────────────────────────────────

    /// <summary>Tri-state result of <see cref="TryGetSourceDateEpoch"/>: unset, or present with a value.</summary>
    public readonly record struct SourceDateEpochValue(bool IsSet, long Value);

    /// <summary>
    /// Reads and parses <see cref="SourceDateEpoch"/>. Returns <c>Success</c> with
    /// <see cref="SourceDateEpochValue.IsSet"/> <c>false</c> when the variable is absent,
    /// <c>Success</c> with the parsed value when present and valid, and <c>Failure</c> (message
    /// prefixed <c>RPR001</c>) when present but not a valid Unix timestamp.
    ///
    /// <para>This method owns the PARSE only. What to do with each outcome is a per-caller
    /// policy decision — the four production call sites disagree on purpose:
    /// <c>PackageBuilder.Reproducible</c>/<c>BundleBuilder.Reproducible</c> throw
    /// (<c>ArgumentException</c> on malformed, <c>InvalidOperationException</c> RPR002 on
    /// absent-with-no-override); <c>BuildCommand.ResolveSourceDateEpoch</c> reports the error and
    /// falls back to <c>git log</c> when absent; <c>ReproducibleSbomIdentity.Resolve</c> silently
    /// falls back to a fresh GUID/UTC-now on EITHER absent or malformed, because SBOM generation
    /// must never fail just because reproducibility wasn't (or couldn't be) established.</para>
    /// </summary>
    public static Result<SourceDateEpochValue> TryGetSourceDateEpoch()
    {
        var raw = GetRaw(SourceDateEpoch);
        if (raw is null)
            return Result<SourceDateEpochValue>.Success(new SourceDateEpochValue(false, 0));

        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return Result<SourceDateEpochValue>.Failure(ErrorKind.Validation,
                $"RPR001: SOURCE_DATE_EPOCH '{raw}' is not a valid Unix timestamp.");
        }

        return Result<SourceDateEpochValue>.Success(new SourceDateEpochValue(true, parsed));
    }

    /// <summary>Sets <see cref="SourceDateEpoch"/> to <paramref name="epoch"/> (seconds since the Unix epoch).</summary>
    public static void SetSourceDateEpoch(long epoch) =>
        SetRaw(SourceDateEpoch, epoch.ToString(CultureInfo.InvariantCulture));

    // ── Presence-only opt-in flags ───────────────────────────────────────

    /// <summary>
    /// Whether <see cref="NoSign"/> is set to any non-empty value. This is presence, not a bool
    /// parse — <c>FALKFORGE_NO_SIGN=0</c> and <c>FALKFORGE_NO_SIGN=false</c> both mean "disabled"
    /// today, matching the pre-migration call sites' <c>!string.IsNullOrEmpty(...)</c> check.
    /// Never fails: an opt-in flag has no malformed state, so there is nothing to validate eagerly.
    /// </summary>
    public static bool IsSigningDisabled() => !string.IsNullOrEmpty(GetRaw(NoSign));

    /// <summary>Sets <see cref="NoSign"/> to request that signing be skipped.</summary>
    public static void DisableSigning() => SetRaw(NoSign, "1");

    /// <summary>
    /// Whether <see cref="GenerateSbom"/> is set to any non-empty value. Presence, not parsing —
    /// see <see cref="IsSigningDisabled"/> for the identical quirk on the sibling flag.
    /// </summary>
    public static bool IsSbomGenerationRequested() => !string.IsNullOrEmpty(GetRaw(GenerateSbom));

    /// <summary>Sets <see cref="GenerateSbom"/> to request an SBOM sidecar.</summary>
    public static void RequestSbomGeneration() => SetRaw(GenerateSbom, "1");

    // ── Startup (eager) validation ───────────────────────────────────────

    /// <summary>
    /// Validates every environment variable that is safe and useful to check BEFORE a process
    /// does any work, aggregating every problem found (not just the first) so an operator fixes
    /// their environment in one pass. Opt-in presence flags (<see cref="NoSign"/>,
    /// <see cref="GenerateSbom"/>) are deliberately excluded: they have no malformed state, so
    /// eager validation has nothing to check and they stay resolved lazily at their point of use.
    /// Path-valued variables (<see cref="EngineStub"/>, <see cref="ElevationCompanion"/>) are also
    /// excluded: their resolution requires filesystem probes with their own detailed fail-loud
    /// messages, and only matters for the one build operation (bundle compilation) that consumes
    /// them — running that probe unconditionally at CLI startup would be surprising for
    /// unrelated commands (e.g. <c>forge validate</c>) and would duplicate the locators'
    /// resolution-order logic here for no benefit.
    /// </summary>
    public static Result<Unit> ValidateEager()
    {
        var problems = new List<string>();

        var epoch = TryGetSourceDateEpoch();
        if (epoch.IsFailure)
            problems.Add(epoch.Error.Message);

        if (problems.Count == 0)
            return Result<Unit>.Success(Unit.Value);

        return Result<Unit>.Failure(ErrorKind.Validation,
            "Environment configuration problems:" + Environment.NewLine +
            string.Join(Environment.NewLine, problems.ConvertAll(p => "  - " + p)));
    }
}
