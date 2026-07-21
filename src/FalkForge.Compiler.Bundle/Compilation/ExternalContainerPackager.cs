using System.Security.Cryptography;
using FalkForge.Engine.Protocol.Manifest;

namespace FalkForge.Compiler.Bundle.Compilation;

/// <summary>
/// Splits the bundle's embeddable payloads into those that stay embedded in the self-extracting exe
/// and those that belong to an <em>external</em> container — a container the author gave a
/// <c>DownloadUrl</c> (A6). Each external container's payloads are written to a standalone container
/// file next to the bundle exe (reusing the FALKBUNDLE payload-embed format via
/// <see cref="PayloadEmbedder"/>, with an empty stub in place of an engine), and described back to the
/// caller as <see cref="ExternalContainerInfo"/> so the manifest can record the URL, the whole-file
/// SHA-256, and the container's membership.
/// <para>
/// The external payloads are removed from the set the exe embeds but are returned separately so the
/// caller still folds them into the ECDSA signature and the SBOM — the engine binds each downloaded
/// container payload back to that signed set before extraction, so an external payload is exactly as
/// trust-covered as an embedded one.
/// </para>
/// </summary>
internal static class ExternalContainerPackager
{
    /// <summary>The build-artifact extension for a standalone external container file.</summary>
    internal const string ContainerFileExtension = ".ffcontainer";

    internal sealed record PackageOutcome
    {
        public required List<PayloadEntry> EmbeddedPayloads { get; init; }
        public required List<PayloadEntry> ExternalPayloads { get; init; }
        public required ExternalContainerInfo[] Containers { get; init; }
    }

    /// <summary>
    /// Partitions <paramref name="embeddablePayloads"/> and, for every external container that has at
    /// least one assigned payload, writes its container file into <paramref name="outputDirectory"/>.
    /// Payloads whose container has no <c>DownloadUrl</c> (or no container at all) stay embedded.
    /// </summary>
    internal static Result<PackageOutcome> Package(
        BundleModel model,
        IReadOnlyList<PayloadEntry> embeddablePayloads,
        string outputDirectory)
    {
        // Only containers carrying a DownloadUrl are external; the rest are pure grouping hints that
        // still embed. Validation (BundleValidator) already guarantees every package ContainerId
        // references a defined container, so a lookup miss here means "local container" (embed).
        var externalContainerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var container in model.Containers)
        {
            if (!string.IsNullOrWhiteSpace(container.DownloadUrl))
                externalContainerIds.Add(container.Id);
        }

        if (externalContainerIds.Count == 0)
            return new PackageOutcome
            {
                EmbeddedPayloads = embeddablePayloads.ToList(),
                ExternalPayloads = [],
                Containers = []
            };

        var embedded = new List<PayloadEntry>();
        var external = new List<PayloadEntry>();
        var membership = new Dictionary<string, List<PayloadEntry>>(StringComparer.Ordinal);

        foreach (var payload in embeddablePayloads)
        {
            if (payload.ContainerId is not null && externalContainerIds.Contains(payload.ContainerId))
            {
                external.Add(payload);
                if (!membership.TryGetValue(payload.ContainerId, out var list))
                {
                    list = [];
                    membership[payload.ContainerId] = list;
                }

                list.Add(payload);
            }
            else
            {
                embedded.Add(payload);
            }
        }

        // A container may carry a DownloadUrl yet have no payload assigned (dead authoring config); it
        // produces no file. That case is surfaced as a warning by the compiler, not an error here.
        if (membership.Count == 0)
            return new PackageOutcome { EmbeddedPayloads = embedded, ExternalPayloads = [], Containers = [] };

        try
        {
            Directory.CreateDirectory(outputDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<PackageOutcome>.Failure(ErrorKind.CompilationError,
                $"Failed to create output directory for external containers: {ex.Message}");
        }

        var infos = new List<ExternalContainerInfo>(membership.Count);
        var embedder = new PayloadEmbedder();

        // One reusable empty stub: an external container is the FALKBUNDLE payload-embed layout with no
        // engine front, so its "stub" is zero bytes and the leading magic sits at offset 0.
        string emptyStub;
        try
        {
            emptyStub = Path.Combine(
                Path.GetTempPath(), $"falkforge-container-stub-{Guid.NewGuid():N}.bin");
            File.WriteAllBytes(emptyStub, []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<PackageOutcome>.Failure(ErrorKind.CompilationError,
                $"Failed to prepare external container stub: {ex.Message}");
        }

        try
        {
            // Emit in the author's declared container order for a deterministic manifest.
            foreach (var container in model.Containers)
            {
                if (!membership.TryGetValue(container.Id, out var containerPayloads))
                    continue;

                var fileName = $"{model.Name}.{container.Id}{ContainerFileExtension}";
                var containerPath = Path.Combine(outputDirectory, fileName);

                var containerManifest = BuildContainerManifest(model, container.Id);
                var embedResult = embedder.Embed(emptyStub, containerPath, containerManifest, containerPayloads);
                if (embedResult.IsFailure)
                    return Result<PackageOutcome>.Failure(embedResult.Error);

                string sha256;
                try
                {
                    using var stream = File.OpenRead(containerPath);
                    sha256 = Convert.ToHexString(SHA256.HashData(stream));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return Result<PackageOutcome>.Failure(ErrorKind.CompilationError,
                        $"Failed to hash external container '{container.Id}': {ex.Message}");
                }

                infos.Add(new ExternalContainerInfo
                {
                    Id = container.Id,
                    // Non-null by construction: container.Id is in externalContainerIds only when
                    // DownloadUrl is non-empty.
                    DownloadUrl = container.DownloadUrl!,
                    Sha256 = sha256,
                    FileName = fileName,
                    PackageIds = containerPayloads.Select(p => p.PackageId).ToArray()
                });
            }
        }
        finally
        {
            try
            {
                if (File.Exists(emptyStub))
                    File.Delete(emptyStub);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup of a temp stub; the container files are already written.
            }
        }

        return new PackageOutcome
        {
            EmbeddedPayloads = embedded,
            ExternalPayloads = external,
            Containers = infos.ToArray()
        };
    }

    /// <summary>
    /// A minimal manifest embedded in the container file. The engine reads only the container's table
    /// of contents to extract payloads, so this carries the bundle's identity (for provenance/debugging)
    /// and no packages. It is never the trust source — the payloads are bound to the bundle's signed
    /// manifest, not this one.
    /// </summary>
    private static InstallerManifest BuildContainerManifest(BundleModel model, string containerId) => new()
    {
        Name = $"{model.Name}:{containerId}",
        Manufacturer = model.Manufacturer,
        Version = model.Version,
        BundleId = model.BundleId,
        UpgradeCode = model.UpgradeCode,
        Packages = [],
        Scope = model.Scope
    };
}
