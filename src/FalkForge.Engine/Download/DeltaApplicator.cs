using System.Security.Cryptography;
using Octodiff.Core;
using Octodiff.Diagnostics;

namespace FalkForge.Engine.Download;

/// <summary>
/// Applies an Octodiff binary delta to basis data and verifies the reconstructed payload hash.
/// </summary>
internal static class DeltaApplicator
{
    public static Result<byte[]> Apply(byte[] basisData, byte[] deltaData, string expectedSha256)
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

            var reconstructed = outputStream.ToArray();

            var actualHash = Convert.ToHexString(SHA256.HashData(reconstructed));
            if (!string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
                return Result<byte[]>.Failure(ErrorKind.PayloadError,
                    $"Delta reconstruction SHA-256 mismatch. Expected {expectedSha256}, got {actualHash}");

            return Result<byte[]>.Success(reconstructed);
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Failure(ErrorKind.BundleError,
                $"Delta application failed: {ex.Message}");
        }
    }
}
