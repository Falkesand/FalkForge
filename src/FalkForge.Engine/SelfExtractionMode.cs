namespace FalkForge.Engine;

using System.Diagnostics;
using FalkForge.Engine.Integrity;
using FalkForge.Engine.Protocol.Bundle;

/// <summary>
/// Self-extraction mode: lists or extracts payloads from the embedded bundle and exits. Extracted
/// from <c>Program.Main</c> (pure move, behavior-preserving) so the list/extract/package-filter/
/// dispatch logic is unit-testable in isolation from process bootstrap.
/// </summary>
internal static class SelfExtractionMode
{
    /// <summary>
    /// Entry point used by <c>Program.Main</c>. Resolves the running bundle's own path via the
    /// standard fallback chain (<see cref="Environment.ProcessPath"/>, then
    /// <see cref="Process.GetCurrentProcess"/>'s main module), exiting 3 if neither resolves.
    /// </summary>
    internal static Task<int> RunAsync(EngineInvocationArgs inv) => RunAsync(inv, selfPathOverride: null);

    /// <summary>
    /// Test seam: <paramref name="selfPathOverride"/> lets a test point extraction at a real bundle
    /// it built, since a unit-test host process is never itself a self-extracting bundle. When null
    /// (the production call site above), behavior is byte-identical to the original inline code —
    /// the fallback chain runs exactly as before.
    /// </summary>
    internal static async Task<int> RunAsync(EngineInvocationArgs inv, string? selfPathOverride)
    {
        var extractDir = inv.ExtractDir;
        var extractList = inv.ExtractList;
        var extractPackages = inv.ExtractPackages;
        var baseBundlePath = inv.BaseBundlePath;
        var requireSigned = inv.RequireSigned;

        var selfPath = selfPathOverride
            ?? Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName;

        if (selfPath is null)
        {
            await Console.Error.WriteLineAsync("Error: Could not determine bundle path.");
            return 3;
        }

        var contentResult = BundleReader.Extract(selfPath);
        if (contentResult.IsFailure)
        {
            await Console.Error.WriteLineAsync($"Error: {contentResult.Error.Message}");
            return 2;
        }

        var content = contentResult.Value;

        if (extractList)
        {
            Console.WriteLine($"Packages in {Path.GetFileName(selfPath)}:");
            foreach (var entry in content.TocEntries)
            {
                var size = entry.OriginalSize < 1024 * 1024
                    ? $"{entry.OriginalSize / 1024.0:F1} KB"
                    : $"{entry.OriginalSize / (1024.0 * 1024.0):F1} MB";
                Console.WriteLine($"  {entry.PackageId,-25} {size,10}");
            }
            return 0;
        }

        // Trust binding: before extracting any payload, bind the value the extractor will
        // verify bytes against (the unsigned overlay TOC hash) to the ECDSA-signed manifest
        // hash. Without this, a validly-signed bundle whose payload bytes + TOC hash were
        // rewritten after signing would extract the tampered bytes. An unsigned bundle passes
        // through (backward compatible).
        var extractTrust = EnginePayloadTrust.VerifySignedPayloadTrust(content, requireSigned);
        if (extractTrust.IsFailure)
        {
            await Console.Error.WriteLineAsync($"Error: {extractTrust.Error.Message}");
            return 2;
        }

        Directory.CreateDirectory(extractDir!);
        var toExtract = content.TocEntries.AsEnumerable();

        if (extractPackages.Count > 0)
        {
            var requested = new HashSet<string>(extractPackages, StringComparer.OrdinalIgnoreCase);
            var available = content.TocEntries.Select(e => e.PackageId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = requested.Except(available).ToList();
            if (missing.Count > 0)
            {
                await Console.Error.WriteLineAsync($"Package(s) not found: {string.Join(", ", missing)}");
                await Console.Error.WriteLineAsync("Available:");
                foreach (var e in content.TocEntries)
                    await Console.Error.WriteLineAsync($"  {e.PackageId}");
                return 1;
            }
            toExtract = content.TocEntries.Where(e => requested.Contains(e.PackageId));
        }

        Console.WriteLine($"Extracting {Path.GetFileName(selfPath)}...");
        foreach (var entry in toExtract)
        {
            // Single-pass: streams decompressed bytes to the file while verifying SHA-256;
            // deletes the partial file and fails on mismatch. The contained overload rejects
            // a crafted PackageId (e.g. "..\..\evil") that would escape extractDir — the TOC
            // is attacker-controlled, so the destination is never composed from it unguarded.
            // Delta entries are reconstructed against --base-bundle instead of written raw.
            var payloadResult = PayloadReconstructionDispatcher.Dispatch(
                selfPath, entry, extractDir!, Path.Combine(entry.PackageId, $"{entry.PackageId}.dat"), baseBundlePath);
            if (payloadResult.IsFailure)
            {
                await Console.Error.WriteLineAsync($"  Failed: {entry.PackageId} — {payloadResult.Error.Message}");
                return 2;
            }

            var sizeStr = entry.OriginalSize < 1024 * 1024
                ? $"{entry.OriginalSize / 1024.0:F1} KB"
                : $"{entry.OriginalSize / (1024.0 * 1024.0):F1} MB";
            Console.WriteLine($"  {entry.PackageId} ({sizeStr}) → {Path.GetDirectoryName(payloadResult.Value)}");
        }

        Console.WriteLine($"Extracted to {extractDir}");
        return 0;
    }
}
