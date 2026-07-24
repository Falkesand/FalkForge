using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using FalkForge.Cli.Settings;
using FalkForge.Cli.Verification;
using FalkForge.Compiler.Msi;
using FalkForge.Engine.Protocol.Bundle;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FalkForge.Cli.Commands;

/// <summary>
/// Independently verifies a shipped artifact. Two modes:
/// <list type="bullet">
///   <item><b>Rebuild-and-compare</b> (<c>--rebuild &lt;project&gt;</c>, .msi or .exe) — rebuilds
///   the project in reproducible mode and byte-compares the result against the supplied artifact.
///   Identical bytes prove the artifact came from that source.</item>
///   <item><b>Signature-only</b> (.msi, no <c>--rebuild</c>) — checks the MSI's embedded or
///   detached pure-.NET ECDSA integrity signature via <see cref="MsiIntegrityVerifier"/>, without
///   needing the source project. See <see cref="ExecuteSignatureVerify"/>.</item>
/// </list>
/// <para>
/// Rebuild-and-compare verdicts and exit codes:
/// <list type="bullet">
///   <item><c>VERIFIED</c> (exit 0) — rebuilt artifact is byte-identical.</item>
///   <item><c>MISMATCH</c> (exit 1) — bytes differ; the diagnostic reports the size delta, total
///   differing-byte count, first differing offsets, and (for bundles/signed MSIs) the region or
///   signature note that explains it.</item>
///   <item><c>REBUILD-FAILED</c> (exit 2) — the rebuild process exited non-zero (build failed).</item>
///   <item><c>SETUP-ERROR</c> (exit 3) — the rebuild succeeded but produced no artifact of the
///   expected type (a project/config mismatch, not a build failure).</item>
///   <item>exit 3 (no verdict) — IO/setup failure before the rebuild: artifact missing, project
///   missing, or epoch unresolved.</item>
/// </list>
/// </para>
/// <para>
/// Signature-only verdicts and exit codes: <c>VERIFIED</c> (exit 0), <c>NOT-SIGNED</c> or
/// <c>FAILED</c> (exit 1 — a signature was found but failed to verify, matched no trusted key, or
/// the payload no longer matches what was signed; fail-loud, no verdict silently passes), or exit
/// 3 for a setup failure (the MSI could not be opened at all).
/// </para>
/// </summary>
[Description("Independently verify a shipped artifact by rebuilding from source and byte-comparing")]
internal sealed class VerifyCommand : Command<VerifySettings>
{
    private static readonly TimeSpan RebuildTimeout = TimeSpan.FromMinutes(5);

    private readonly IConsoleOutput _output;
    private readonly System.IO.TextWriter _jsonSink;
    private readonly IRebuildRunner _runner;
    private readonly string? _gitWorkingDirectory;

    public VerifyCommand() : this(new SpectreConsoleOutput()) { }

    public VerifyCommand(
        IConsoleOutput output,
        System.IO.TextWriter? jsonSink = null,
        IRebuildRunner? runner = null,
        string? gitWorkingDirectory = null)
    {
        _output = output;
        _jsonSink = jsonSink ?? Console.Out;
        _runner = runner ?? new DefaultRebuildRunner();
        _gitWorkingDirectory = gitWorkingDirectory;
    }

    protected override int Execute(
        [NotNull] CommandContext context,
        [NotNull] VerifySettings settings,
        CancellationToken cancellationToken)
    {
        var jsonOutput = settings.Json ? new JsonConsoleOutput() : null;
        var output = (IConsoleOutput?)jsonOutput ?? _output;

        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        var exitCode = ExecuteCore(settings, output, result, cancellationToken);

        if (jsonOutput is not null)
            _jsonSink.WriteLine(jsonOutput.WriteEnvelope("verify", exitCode, result));

        return exitCode;
    }

    private int ExecuteCore(
        VerifySettings settings,
        IConsoleOutput output,
        IDictionary<string, string?> result,
        CancellationToken ct)
    {
        var artifactPath = Path.GetFullPath(settings.ArtifactPath);
        if (!File.Exists(artifactPath))
        {
            output.WriteError($"File not found: {artifactPath}");
            return ExitCodes.RuntimeError;
        }

        if (string.IsNullOrWhiteSpace(settings.RebuildProjectPath))
            return ExecuteSignatureVerify(settings, output, result, artifactPath);

        var projectPath = Path.GetFullPath(settings.RebuildProjectPath);
        if (!File.Exists(projectPath))
        {
            output.WriteError($"Rebuild project not found: {projectPath}");
            return ExitCodes.RuntimeError;
        }

        // Resolve the epoch: explicit override wins, else env var / git HEAD (same rules as build).
        long epoch;
        if (settings.SourceDateEpoch is { } explicitEpoch)
        {
            epoch = explicitEpoch;
        }
        else
        {
            var resolved = BuildCommand.ResolveSourceDateEpoch(output, _gitWorkingDirectory);
            if (resolved is null)
                return ExitCodes.RuntimeError; // ResolveSourceDateEpoch already wrote RPR001/RPR002.
            epoch = resolved.Value;
        }

        var artifactExt = Path.GetExtension(artifactPath);
        var tempDir = Path.Combine(Path.GetTempPath(), "FalkForge", $"verify_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            return ExecuteWithTempDir(output, result, artifactPath, projectPath, artifactExt, epoch, tempDir, ct);
        }
        finally
        {
            // Best-effort cleanup of the scratch directory; a cleanup failure must not turn a
            // completed verification into an error.
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch (IOException) { /* best-effort cleanup — failure must not mask a completed verification */ }
            catch (UnauthorizedAccessException) { /* best-effort cleanup — failure must not mask a completed verification */ }
        }
    }

    private int ExecuteWithTempDir(
        IConsoleOutput output,
        IDictionary<string, string?> result,
        string artifactPath,
        string projectPath,
        string artifactExt,
        long epoch,
        string tempDir,
        CancellationToken ct)
    {
        output.MarkupLine($"[grey]Rebuilding {Markup.Escape(Path.GetFileName(projectPath))} (SOURCE_DATE_EPOCH={epoch})...[/]");

        RebuildResult rebuild;
        try
        {
            rebuild = _runner.RebuildAsync(projectPath, tempDir, epoch, RebuildTimeout, ct)
                .GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            output.WriteError("Verification was cancelled.");
            return ExitCodes.RuntimeError;
        }
        catch (Exception ex)
        {
            output.WriteError($"Failed to launch rebuild: {ex.Message}");
            return ExitCodes.RuntimeError;
        }

        if (rebuild.ExitCode != 0)
        {
            result["verdict"] = "REBUILD-FAILED";
            output.WriteError("REBUILD-FAILED: the project did not build successfully.");
            if (!string.IsNullOrWhiteSpace(rebuild.Stderr))
                output.WriteError(rebuild.Stderr.Trim());
            else if (!string.IsNullOrWhiteSpace(rebuild.Stdout))
                output.WriteError(rebuild.Stdout.Trim());
            return ExitCodes.CompilationError;
        }

        // Locate the rebuilt artifact: same extension as the artifact under verification.
        var rebuiltPath = Directory
            .EnumerateFiles(tempDir, $"*{artifactExt}", SearchOption.AllDirectories)
            .FirstOrDefault();

        if (rebuiltPath is null)
        {
            // The rebuild process exited 0 — the build itself succeeded — but emitted no
            // artifact of the expected type. This is a setup/config mismatch, not a build
            // failure, so it gets a distinct verdict (SETUP-ERROR) at exit 3. Reusing
            // REBUILD-FAILED here would map one verdict to two exit codes (2 and 3).
            result["verdict"] = "SETUP-ERROR";
            output.WriteError(
                $"SETUP-ERROR: rebuild succeeded but produced no {artifactExt} artifact. " +
                "Ensure the project builds the same artifact type as the one being verified.");
            return ExitCodes.RuntimeError;
        }

        var report = ArtifactComparer.Compare(artifactPath, rebuiltPath);
        result["expectedSize"] = report.ExpectedSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
        result["actualSize"] = report.ActualSize.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (report.Identical)
        {
            result["verdict"] = "VERIFIED";
            output.MarkupLine($"[green]VERIFIED:[/] rebuilt artifact is byte-identical ({report.ExpectedSize:N0} bytes). The artifact provably came from this source.");
            return ExitCodes.Success;
        }

        return ReportMismatch(output, result, rebuiltPath, artifactExt, report);
    }

    /// <summary>
    /// Emits the MISMATCH diagnostic and returns the validation-failure exit code. For bundles it
    /// adds a structural region hint and, when the rebuilt bundle is ECDSA-signed, an honest note
    /// that signed bundles are inherently non-deterministic.
    /// </summary>
    private static int ReportMismatch(
        IConsoleOutput output,
        IDictionary<string, string?> result,
        string rebuiltPath,
        string artifactExt,
        ComparisonReport report)
    {
        result["verdict"] = "MISMATCH";
        result["sizeDelta"] = report.SizeDelta.ToString(System.Globalization.CultureInfo.InvariantCulture);
        result["differingBytes"] = report.DifferingByteCount.ToString(System.Globalization.CultureInfo.InvariantCulture);

        output.MarkupLine($"[red]MISMATCH:[/] rebuilt artifact differs from the shipped artifact.");
        output.MarkupLine($"[grey]Shipped size:[/] {report.ExpectedSize:N0} bytes   [grey]Rebuilt size:[/] {report.ActualSize:N0} bytes   [grey]Delta:[/] {report.SizeDelta:+#;-#;0} bytes");
        output.MarkupLine($"[grey]Differing bytes:[/] {report.DifferingByteCount:N0}");

        if (report.FirstDifferingOffsets.Count > 0)
        {
            var offsets = string.Join(", ", report.FirstDifferingOffsets.Select(o => $"0x{o:X}"));
            output.MarkupLine($"[grey]First differing offset(s):[/] {Markup.Escape(offsets)}");
            result["firstDifferingOffset"] = report.FirstDifferingOffsets[0].ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        // Bundle-aware diagnostics: classify the first differing offset and detect signed bundles.
        if (artifactExt.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            && report.FirstDifferingOffsets.Count > 0)
        {
            AddBundleDiagnostics(output, result, rebuiltPath, report.FirstDifferingOffsets[0]);
        }
        else if (artifactExt.Equals(".msi", StringComparison.OrdinalIgnoreCase))
        {
            AddMsiDiagnostics(output, result, rebuiltPath);
        }

        return ExitCodes.ValidationFailure;
    }

    /// <summary>
    /// MSI counterpart to <see cref="AddBundleDiagnostics"/>: when the rebuilt MSI carries an
    /// embedded <c>_FalkForgeIntegrity</c>/<c>ManifestSignature</c> row, a byte mismatch may be
    /// entirely explained by the signature itself — ECDSA is non-deterministic, so a signed MSI can
    /// never byte-match a prior build even from identical source. Checks the TABLE specifically
    /// (not merely "was Integrity() configured") so the note only fires when the signature actually
    /// lives in-band and could plausibly account for the differing bytes.
    /// </summary>
    private static void AddMsiDiagnostics(
        IConsoleOutput output, IDictionary<string, string?> result, string rebuiltMsiPath)
    {
        if (!OperatingSystem.IsWindows())
            return; // Reading the _FalkForgeIntegrity table requires msi.dll.

        var dbResult = MsiDatabase.Open(rebuiltMsiPath, readOnly: true);
        if (dbResult.IsFailure)
            return; // Not a readable MSI database — offsets already reported.

        using var db = dbResult.Value;
        var rowsResult = db.QueryRows("SELECT `Id` FROM `_FalkForgeIntegrity`", 1);
        if (rowsResult.IsFailure || !rowsResult.Value.Any(r => r[0] == "ManifestSignature"))
            return;

        result["signed"] = "true";
        output.MarkupLine(
            "[yellow]Note:[/] the rebuilt MSI carries an embedded ECDSA integrity signature " +
            "(_FalkForgeIntegrity/ManifestSignature). Like bundle ECDSA signatures, this signature is " +
            "non-deterministic across builds, so an Integrity()-only MSI can never byte-match a prior " +
            "build. Combine Reproducible() with Integrity() to get a byte-identical MSI plus a detached " +
            "'<msi>.sig.json' sidecar signature instead (the signature moves out-of-band, so it no " +
            "longer affects the compared bytes). Either way, use 'forge verify <msi>' (no --rebuild) to " +
            "check the signature's validity directly, or set FALKFORGE_NO_SIGN to compare unsigned bytes.");
    }

    /// <summary>
    /// Signature-only verification: checks an MSI's embedded or detached pure-.NET ECDSA integrity
    /// signature via <see cref="MsiIntegrityVerifier"/> instead of rebuilding from source. Selected
    /// when <c>--rebuild</c> is omitted (see <see cref="ExecuteCore"/>).
    /// </summary>
    private static int ExecuteSignatureVerify(
        VerifySettings settings,
        IConsoleOutput output,
        IDictionary<string, string?> result,
        string artifactPath)
    {
        if (!artifactPath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
        {
            output.WriteError(
                "Signature-only verification (no --rebuild) is only supported for .msi artifacts today. " +
                "Pass --rebuild <project> to verify a .exe bundle.");
            return ExitCodes.RuntimeError;
        }

        if (!OperatingSystem.IsWindows())
        {
            output.WriteError("MSI signature verification requires Windows (msi.dll).");
            return ExitCodes.RuntimeError;
        }

        var trustedKeys = new HashSet<string>(settings.TrustedKeys, StringComparer.OrdinalIgnoreCase);
        var verifyResult = MsiIntegrityVerifier.Verify(artifactPath, trustedKeys);
        if (verifyResult.IsFailure)
        {
            output.WriteError(verifyResult.Error.Message);
            return ExitCodes.FromErrorKind(verifyResult.Error.Kind);
        }

        var outcome = verifyResult.Value;
        result["verdict"] = outcome.Verdict switch
        {
            SignatureVerdict.Verified => "VERIFIED",
            SignatureVerdict.NotSigned => "NOT-SIGNED",
            _ => "FAILED"
        };
        if (outcome.FormatTag is { } formatTag)
            result["formatTag"] = formatTag;
        if (outcome.MatchedFingerprint is { } fingerprint)
            result["fingerprint"] = fingerprint;
        // Fail-loud for JSON/automation consumers too (Merge Gate MEDIUM finding): a machine reading
        // only `verdict` must not conflate "payload is self-consistent" with "publisher identity
        // confirmed" — those are different guarantees. Always present (not just on Verified) so an
        // automated consumer never has to infer it from field absence.
        result["authorshipEstablished"] = (outcome.MatchedFingerprint is not null).ToString(System.Globalization.CultureInfo.InvariantCulture);

        switch (outcome.Verdict)
        {
            case SignatureVerdict.Verified when outcome.MatchedFingerprint is { } matchedFingerprint:
                // A trusted key matched: authorship is established, not merely internal consistency.
                output.MarkupLine($"[green]VERIFIED (authorship verified):[/] {Markup.Escape(outcome.Message)}");
                if (outcome.Source is { } trustedSource)
                    output.MarkupLine($"[grey]Signature source:[/] {Markup.Escape(trustedSource)}");
                output.MarkupLine($"[grey]Matched trusted key fingerprint:[/] {Markup.Escape(matchedFingerprint)}");
                return ExitCodes.Success;

            case SignatureVerdict.Verified:
                // No --trusted-key was supplied: this PASS proves the payload matches the signed
                // declaration and the signature is internally self-consistent — tamper-evidence, NOT
                // proof of who signed it. Rendered in yellow with an explicit label rather than the
                // same green "VERIFIED" a trusted-key PASS gets (Merge Gate MEDIUM finding): printing
                // an identical label for both would let a consistency-only PASS pass for an
                // authorship-confirmed one — the exact downgrade-attack UX a user must never see.
                output.MarkupLine(
                    "[yellow]VERIFIED (tamper-evidence only — authorship NOT established; pass " +
                    $"--trusted-key to verify publisher):[/] {Markup.Escape(outcome.Message)}");
                if (outcome.Source is { } consistencySource)
                    output.MarkupLine($"[grey]Signature source:[/] {Markup.Escape(consistencySource)}");
                return ExitCodes.Success;

            case SignatureVerdict.NotSigned:
                output.MarkupLine($"[yellow]NOT-SIGNED:[/] {Markup.Escape(outcome.Message)}");
                return ExitCodes.ValidationFailure;

            default: // Failed
                output.MarkupLine($"[red]FAILED:[/] {Markup.Escape(outcome.Message)}");
                return ExitCodes.ValidationFailure;
        }
    }

    private static void AddBundleDiagnostics(
        IConsoleOutput output,
        IDictionary<string, string?> result,
        string rebuiltBundlePath,
        long firstOffset)
    {
        var content = BundleReader.Extract(rebuiltBundlePath);
        if (content.IsFailure)
            return; // Not a readable bundle (e.g. plain EXE) — offsets already reported.

        var totalLength = new FileInfo(rebuiltBundlePath).Length;
        var tocOffset = content.Value.TocEntries.Length > 0
            ? content.Value.TocEntries.Min(e => e.Offset)
            : totalLength - 24;

        var region = BundleRegionHint.Classify(totalLength, tocOffset, firstOffset);
        result["region"] = region;
        output.MarkupLine($"[grey]First difference falls in the[/] [yellow]{Markup.Escape(region)}[/] [grey]region.[/]");

        if (BundleRegionHint.ManifestIsSigned(content.Value.ManifestJsonBytes))
        {
            result["signed"] = "true";
            output.MarkupLine(
                "[yellow]Note:[/] the rebuilt bundle is ECDSA-signed (ManifestSignature present). " +
                "ECDSA signatures are non-deterministic, so a signed bundle can never byte-match across builds. " +
                "Rebuild the project with the FALKFORGE_NO_SIGN environment variable set to verify the unsigned content.");
        }
    }
}
