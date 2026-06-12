using System.Runtime.Versioning;
using FalkForge.Platform.Windows;
using Xunit;

namespace FalkForge.Platform.Windows.Tests;

/// <summary>
/// Verifies that <see cref="AuthenticodeValidator"/> performs real WinVerifyTrust-backed
/// signature validation: an unsigned or tampered file must be rejected (these are always
/// runnable), a genuinely embedded-signed file must pass, and thumbprint pinning must layer
/// on top of — not replace — the trust verification.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AuthenticodeValidatorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AuthenticodeValidator _validator = new();

    public AuthenticodeValidatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"FalkAuthTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; a locked temp file must not fail the test run.
        }
    }

    /// <summary>
    /// A reliably embedded-signed (NOT catalog-signed) file present on dev and CI runners.
    /// dotnet.exe carries an embedded Authenticode signature; catalog-signed system files
    /// such as kernel32.dll would FAIL WTD_CHOICE_FILE verification, so they are unsuitable.
    /// Returns null when the file is absent (tests guard on this and skip the positive case).
    /// </summary>
    private static string? EmbeddedSignedFile()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var candidate = Path.Combine(programFiles, "dotnet", "dotnet.exe");
        return File.Exists(candidate) ? candidate : null;
    }

    [Fact]
    public void ValidateSignature_MissingFile_ReturnsFileNotFound()
    {
        var missing = Path.Combine(_tempDir, "does-not-exist.exe");

        var result = _validator.ValidateSignature(missing, expectedThumbprint: null);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.FileNotFound, result.Error.Kind);
    }

    [Fact]
    public void ValidateSignature_UnsignedFile_ReturnsSecurityError()
    {
        // An arbitrary file with no Authenticode signature must be rejected by WinVerifyTrust.
        var unsigned = Path.Combine(_tempDir, "unsigned.bin");
        File.WriteAllBytes(unsigned, new byte[] { 0x4D, 0x5A, 0x00, 0x01, 0x02, 0x03 });

        var result = _validator.ValidateSignature(unsigned, expectedThumbprint: null);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
    }

    [Fact]
    public void ValidateSignature_TamperedSignedFile_ReturnsSecurityError()
    {
        var signed = EmbeddedSignedFile();
        if (signed is null)
            Assert.Skip("No embedded-signed reference file (dotnet.exe) found on this machine — positive-path test skipped.");

        // Copy a genuinely-signed file, then flip a byte in the middle. The embedded signature
        // no longer matches the file hash, so WinVerifyTrust must reject it.
        var tampered = Path.Combine(_tempDir, "tampered.exe");
        var bytes = File.ReadAllBytes(signed);
        bytes[bytes.Length / 2] ^= 0xFF;
        File.WriteAllBytes(tampered, bytes);

        var result = _validator.ValidateSignature(tampered, expectedThumbprint: null);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
    }

    [Fact]
    public void ValidateSignature_EmbeddedSignedFile_NoThumbprint_Succeeds()
    {
        var signed = EmbeddedSignedFile();
        if (signed is null)
            Assert.Skip("No embedded-signed reference file (dotnet.exe) found on this machine — positive-path test skipped.");

        var result = _validator.ValidateSignature(signed, expectedThumbprint: null);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public void ValidateSignature_EmbeddedSignedFile_WrongThumbprint_ReturnsSecurityError()
    {
        var signed = EmbeddedSignedFile();
        if (signed is null)
            Assert.Skip("No embedded-signed reference file (dotnet.exe) found on this machine — positive-path test skipped.");

        // Trust verification passes, but the pinned thumbprint does not match the signer —
        // pinning must layer on top of (not bypass) a successful WinVerifyTrust result.
        var result = _validator.ValidateSignature(
            signed, expectedThumbprint: "0000000000000000000000000000000000000000");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
    }
}
