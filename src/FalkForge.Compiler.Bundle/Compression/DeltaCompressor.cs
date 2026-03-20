using Octodiff.Core;
using Octodiff.Diagnostics;

namespace FalkForge.Compiler.Bundle.Compression;

/// <summary>
/// Creates binary deltas between old and new payload data using the Octodiff rsync algorithm.
/// </summary>
public static class DeltaCompressor
{
    /// <summary>
    /// Creates a binary delta between the basis (old) and new data.
    /// </summary>
    public static Result<byte[]> CreateDelta(byte[] basisData, byte[] newData)
    {
        try
        {
            // Step 1: Generate signature from basis
            using var basisStream = new MemoryStream(basisData);
            using var signatureStream = new MemoryStream();
            var signatureBuilder = new SignatureBuilder();
            signatureBuilder.Build(basisStream, new SignatureWriter(signatureStream));

            // Step 2: Create delta
            signatureStream.Position = 0;
            using var newStream = new MemoryStream(newData);
            using var deltaStream = new MemoryStream();
            var deltaBuilder = new DeltaBuilder();
            deltaBuilder.BuildDelta(
                newStream,
                new SignatureReader(signatureStream, NullProgressReporter.Instance),
                new AggregateCopyOperationsDecorator(new BinaryDeltaWriter(deltaStream)));

            return Result<byte[]>.Success(deltaStream.ToArray());
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Failure(ErrorKind.BundleError,
                $"Delta compression failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Applies a delta to the basis (old) data to reconstruct the new data.
    /// </summary>
    public static Result<byte[]> ApplyDelta(byte[] basisData, byte[] deltaData)
    {
        try
        {
            using var basisStream = new MemoryStream(basisData);
            using var deltaStream = new MemoryStream(deltaData);
            using var outputStream = new MemoryStream();

            var deltaApplier = new DeltaApplier { SkipHashCheck = false };
            deltaApplier.Apply(
                basisStream,
                new BinaryDeltaReader(deltaStream, NullProgressReporter.Instance),
                outputStream);

            return Result<byte[]>.Success(outputStream.ToArray());
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Failure(ErrorKind.BundleError,
                $"Delta application failed: {ex.Message}");
        }
    }
}
