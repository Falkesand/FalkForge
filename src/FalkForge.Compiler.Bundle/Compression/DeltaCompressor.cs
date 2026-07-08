using Octodiff.Core;
using Octodiff.Diagnostics;

namespace FalkForge.Compiler.Bundle.Compression;

/// <summary>
/// Creates binary deltas between old and new payload data using the Octodiff rsync algorithm.
/// </summary>
public static class DeltaCompressor
{
    /// <summary>
    /// Creates a binary delta between the basis (old) and new streams, writing the delta bytes to
    /// <paramref name="outputStream"/>. Callers should pass file-backed streams for
    /// <paramref name="basisStream"/>/<paramref name="newStream"/>/<paramref name="outputStream"/>
    /// so the (potentially large) basis/new payload bytes are never buffered in memory — only the
    /// rsync signature (proportional to the basis size, not the whole file) is held in memory.
    /// <paramref name="basisStream"/> is fully consumed here (signature building only); it is not
    /// touched again during delta construction.
    /// </summary>
    public static Result<Unit> CreateDelta(Stream basisStream, Stream newStream, Stream outputStream)
    {
        try
        {
            // Signature size is proportional to the basis (roughly 1/32 by default block size),
            // not the whole payload, so buffering it in memory is intentional and cheap.
            using var signatureStream = new MemoryStream();
            var signatureBuilder = new SignatureBuilder();
            signatureBuilder.Build(basisStream, new SignatureWriter(signatureStream));

            signatureStream.Position = 0;
            var deltaBuilder = new DeltaBuilder();
            deltaBuilder.BuildDelta(
                newStream,
                new SignatureReader(signatureStream, NullProgressReporter.Instance),
                new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(outputStream)));

            return Unit.Value;
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure(ErrorKind.BundleError,
                $"Delta compression failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Applies a delta to the basis (old) stream, writing the reconstructed data to
    /// <paramref name="outputStream"/>. <paramref name="basisStream"/> must support seeking —
    /// Octodiff's copy operations reference arbitrary offsets into it.
    /// </summary>
    public static Result<Unit> ApplyDelta(Stream basisStream, Stream deltaStream, Stream outputStream)
    {
        try
        {
            var deltaApplier = new DeltaApplier { SkipHashCheck = false };
            deltaApplier.Apply(
                basisStream,
                new BinaryDeltaReader(deltaStream, NullProgressReporter.Instance),
                outputStream);

            return Unit.Value;
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure(ErrorKind.BundleError,
                $"Delta application failed: {ex.Message}");
        }
    }
}
