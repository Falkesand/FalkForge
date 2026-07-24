namespace FalkForge.Engine;

/// <summary>
/// Command-line flags consumed by the engine's own invocation (self-extract mode, bootstrapper
/// hand-off, and the direct-manifest run path). Extracted from the inline switch loop in
/// <c>Program.Main</c> so the parse is unit-testable in isolation; the parsing rules — including the
/// asymmetric guarding described below — are unchanged from the original inline loop (pure move).
///
/// <para><b>Asymmetric guarding (preserved, not "fixed").</b> <see cref="PipeName"/>,
/// <see cref="ManifestPath"/>, <see cref="PlanOutputPath"/>, <see cref="SbomOutputPath"/>, and
/// <see cref="BaseBundlePath"/> are GUARDED — a trailing flag with no following value is silently
/// ignored. <see cref="SecretPipeName"/>, <see cref="ExtractDir"/>, and package entries in
/// <see cref="ExtractPackages"/> are UNGUARDED — a trailing flag with no following value throws
/// <see cref="IndexOutOfRangeException"/>. This mirrors the original inline loop exactly.</para>
///
/// <para>Note: the separate logging-related flags (<c>--log</c>, <c>--log-level</c>, etc.) are parsed
/// by <c>ProgramArgs.Parse</c>, not here; this loop only skips their values so it does not mistake a
/// value for a standalone flag on the next iteration.</para>
/// </summary>
internal sealed record EngineInvocationArgs(
    string? PipeName,
    string? SecretPipeName,
    string? ManifestPath,
    bool PlanOnly,
    string? PlanOutputPath,
    string? SbomOutputPath,
    string? ExtractDir,
    bool ExtractList,
    IReadOnlyList<string> ExtractPackages,
    string? BaseBundlePath,
    bool RequireSigned)
{
    /// <summary>
    /// Parses the engine's own invocation flags from <paramref name="args"/>. Unknown flags are
    /// silently ignored. See the type doc for the guarded/unguarded asymmetry preserved from the
    /// original inline parser.
    /// </summary>
    internal static EngineInvocationArgs Parse(string[] args)
    {
        string? pipeName = null;
        string? secretPipeName = null;
        string? manifestPath = null;
        var planOnly = false;
        string? planOutputPath = null;
        string? sbomOutputPath = null;
        string? extractDir = null;
        var extractList = false;
        var extractPackages = new List<string>();
        string? baseBundlePath = null;
        // Set by the update launcher (DefaultUpdateLauncher) when it relaunches a downloaded update
        // bundle. On this path a signature is mandatory (C14 Stage 2 / B2): a stripped/unsigned or
        // untrusted-signed update is rejected before any payload is extracted or executed. A fresh
        // install never receives this flag, so an unsigned bundle the user chose to run still installs.
        var requireSigned = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pipe":
                    if (i + 1 < args.Length) pipeName = args[++i];
                    break;
                case "--secret-pipe":
                    secretPipeName = args[++i];
                    break;
                // SECURITY: DEPRECATED — --secret is accepted for backward compatibility but the
                // value is discarded. The engine uses the init-pipe pattern (like Engine.Elevation)
                // to receive secrets over a short-lived pipe instead of command-line arguments,
                // which are visible in process listings and event logs.
                case "--secret":
                    if (i + 1 < args.Length) _ = args[++i]; // consume and discard
                    break;
                case "--manifest":
                    if (i + 1 < args.Length) manifestPath = args[++i];
                    break;
                case "--plan-only":
                    planOnly = true;
                    break;
                case "--plan-output":
                    if (i + 1 < args.Length) planOutputPath = args[++i];
                    break;
                case "--sbom":
                    if (i + 1 < args.Length) sbomOutputPath = args[++i];
                    break;
                case "--extract":
                    extractDir = args[++i];
                    break;
                case "--extract-list":
                    extractList = true;
                    break;
                case "--package":
                    extractPackages.Add(args[++i]);
                    break;
                // Path to the previously-installed (base) bundle, supplied by the update launcher
                // when relaunching a delta update. Delta payloads are reconstructed against this
                // base bundle's payloads; without it a delta payload cannot be applied.
                case "--base-bundle":
                    if (i + 1 < args.Length) baseBundlePath = args[++i];
                    break;
                // Asserted by the update launcher for a downloaded update bundle: require a valid,
                // trusted signature before extracting or executing any payload (C14 Stage 2 / B2).
                case "--require-signed":
                    requireSigned = true;
                    break;
                // Logging flags are parsed by ProgramArgs.Parse above. Consume their
                // values here so the inline parser does not mistake the value for a
                // standalone flag on the next iteration.
                case "--log":
                case "/log":
                case "/L":
                case "--log-level":
                case "/lv":
                    if (i + 1 < args.Length) i++;
                    break;
            }
        }

        return new EngineInvocationArgs(
            pipeName, secretPipeName, manifestPath, planOnly, planOutputPath, sbomOutputPath,
            extractDir, extractList, extractPackages, baseBundlePath, requireSigned);
    }
}
