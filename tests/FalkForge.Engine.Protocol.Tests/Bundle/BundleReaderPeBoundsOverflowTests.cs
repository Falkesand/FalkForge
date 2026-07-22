using System.Reflection;
using FalkForge.Engine.Protocol.Bundle;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Bundle;

/// <summary>
/// <c>TryFindTrailingCertTableOffset</c> (private, in <c>BundleReader.cs</c>) reads the PE optional
/// header's Security data directory. <c>peOffset</c> comes from the untrusted DOS header
/// <c>e_lfanew</c> field — fully attacker-controlled, since the stream is opened directly on
/// on-disk bytes from an arbitrary bundle path. The method's own bounds checks must
/// authoritatively reject an out-of-range <c>peOffset</c> BEFORE any further seek/read is
/// attempted, rather than relying on the <see cref="EndOfStreamException"/> a bogus Seek+Read
/// happens to throw today — which callers (<c>HasBundleFooter</c>/<c>Extract</c>) only catch as an
/// incidental side effect, not because the bounds check itself rejected the input.
/// <para>
/// Reflection is required because the method is intentionally private — there is no public
/// surface for this internal PE-parsing step. Acceptable in test code only: this project is not
/// part of the NativeAOT-published Engine.Protocol assembly.
/// </para>
/// </summary>
public sealed class BundleReaderPeBoundsOverflowTests : IDisposable
{
    private readonly string _tempDir;

    public BundleReaderPeBoundsOverflowTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"BundleReaderPeOverflow_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    /// <summary>
    /// e_lfanew = 0xFFFFFFF0 in a tiny 128-byte file: under uint arithmetic, peOffset +
    /// PeSignatureSize(4) + CoffHeaderSize(20) wraps to 8, which is &lt;= the file length — an
    /// unsigned-arithmetic bounds check would wrongly pass and let the method go on to seek+read
    /// at a ~4 GiB offset in a 128-byte file (only saved by an incidental EndOfStreamException).
    /// The hardened check casts peOffset to long before the addition, so it authoritatively
    /// rejects the out-of-range offset — no exception should ever leave the method.
    /// </summary>
    [Fact]
    public void TryFindTrailingCertTableOffset_WrappingPeOffset_RejectsWithoutThrowing()
    {
        var path = Path.Combine(_tempDir, "crafted.bin");
        var bytes = new byte[128];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        BitConverter.GetBytes(0xFFFFFFF0u).CopyTo(bytes, 0x3C); // e_lfanew — attacker-controlled
        File.WriteAllBytes(path, bytes);

        using var stream = File.OpenRead(path);

        var method = typeof(BundleReader).GetMethod(
            "TryFindTrailingCertTableOffset", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "TryFindTrailingCertTableOffset not found by reflection — has its signature changed?");

        var parameters = new object?[] { stream, 0L };

        bool result;
        try
        {
            result = (bool)method.Invoke(null, parameters)!;
        }
        catch (TargetInvocationException ex)
        {
            Assert.Fail(
                "Bounds check let a wrapping peOffset through instead of rejecting it directly — " +
                $"inner call threw {ex.InnerException?.GetType().Name}: {ex.InnerException?.Message}");
            return;
        }

        Assert.False(result,
            "A peOffset that wraps under uint arithmetic must be rejected by the bounds check itself, " +
            "not merely survive by luck of an incidental exception downstream.");
    }
}
