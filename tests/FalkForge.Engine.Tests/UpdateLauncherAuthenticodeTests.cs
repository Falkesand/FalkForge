namespace FalkForge.Engine.Tests;

using FalkForge.Platform.Windows;
using Xunit;

/// <summary>
/// Verifies that <see cref="DefaultUpdateLauncher"/> enforces Authenticode signature
/// validation before launching a downloaded update bundle.
///
/// Security rationale: a MITM-intercepted or cache-poisoned update EXE must never
/// execute. Signature verification is the last defence before arbitrary code runs
/// elevated on the user's machine (CVE class: CWE-494 Download of Code Without
/// Integrity Check).
/// </summary>
public sealed class UpdateLauncherAuthenticodeTests
{
    // ── Fakes ─────────────────────────────────────────────────────────────

    private sealed class AlwaysValidValidator : IAuthenticodeValidator
    {
        public Result<Unit> ValidateSignature(string filePath, string? expectedThumbprint, string? expectedPublicKeyHash)
            => Unit.Value;
    }

    private sealed class AlwaysInvalidValidator : IAuthenticodeValidator
    {
        public Result<Unit> ValidateSignature(string filePath, string? expectedThumbprint, string? expectedPublicKeyHash)
            => Result<Unit>.Failure(ErrorKind.SecurityError,
                $"File is not signed or has invalid signature: {filePath}");
    }

    private sealed class ThumbprintMismatchValidator : IAuthenticodeValidator
    {
        public Result<Unit> ValidateSignature(string filePath, string? expectedThumbprint, string? expectedPublicKeyHash)
        {
            if (expectedThumbprint is not null &&
                !string.Equals(expectedThumbprint, "AABBCC", StringComparison.OrdinalIgnoreCase))
                return Result<Unit>.Failure(ErrorKind.SecurityError,
                    $"Certificate thumbprint mismatch. Expected: {expectedThumbprint}, Actual: AABBCC");
            return Unit.Value;
        }
    }

    // ── Helper ────────────────────────────────────────────────────────────

    /// <summary>Creates a real empty file on disk so path-containment + file-exists checks pass.</summary>
    private static (string CacheRoot, string FilePath) CreateTempFile()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "FalkForge_AuthTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cacheRoot);
        var filePath = Path.Combine(cacheRoot, "update.exe");
        File.WriteAllBytes(filePath, [0x4D, 0x5A]); // minimal PE header stub
        return (cacheRoot, filePath);
    }

    // ── Tests ─────────────────────────────────────────────────────────────

    [Fact]
    public void Launch_InvalidSignature_ReturnsSecurityErrorWithoutStartingProcess()
    {
        // Arrange
        var (cacheRoot, filePath) = CreateTempFile();
        try
        {
            var launcher = new DefaultUpdateLauncher(cacheRoot, new AlwaysInvalidValidator());

            // Act
            var result = launcher.Launch(filePath);

            // Assert — launch must be refused with SecurityError.
            // Process.Start is never reached because the validator short-circuits.
            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
            // Error code for signature failure
            Assert.Contains("UPD006", result.Error.Message);
        }
        finally
        {
            Directory.Delete(cacheRoot, recursive: true);
        }
    }

    [Fact]
    public void Launch_ValidSignature_NoPinnedThumbprint_Succeeds()
    {
        // Arrange — a valid signature with no pinned publisher passes verification.
        // Process.Start is NOT called here (the file is a stub, not a real EXE),
        // so this test only verifies that the Result returned is not a security failure.
        // We cannot assert Process.Start is called without a real signed EXE; we assert
        // the launcher does NOT return a security error, which is the observable contract.
        var (cacheRoot, filePath) = CreateTempFile();
        try
        {
            var launcher = new DefaultUpdateLauncher(cacheRoot, new AlwaysValidValidator());

            // Act — will attempt Process.Start on stub EXE; may throw/fail with EngineError,
            // but must NOT return SecurityError
            var result = launcher.Launch(filePath);

            // Assert — whatever the launch outcome, it must not be a security refusal
            if (!result.IsSuccess)
                Assert.NotEqual(ErrorKind.SecurityError, result.Error.Kind);
        }
        finally
        {
            Directory.Delete(cacheRoot, recursive: true);
        }
    }

    [Fact]
    public void Launch_PinnedThumbprintMismatch_ReturnsSecurityError()
    {
        // Arrange — the manifest pins a specific thumbprint that does not match the cert.
        var (cacheRoot, filePath) = CreateTempFile();
        try
        {
            const string pinnedThumbprint = "DEADBEEFDEADBEEFDEADBEEFDEADBEEFDEADBEEF";
            var launcher = new DefaultUpdateLauncher(cacheRoot, new ThumbprintMismatchValidator(), pinnedThumbprint);

            // Act
            var result = launcher.Launch(filePath);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
            Assert.Contains("UPD006", result.Error.Message);
        }
        finally
        {
            Directory.Delete(cacheRoot, recursive: true);
        }
    }

    [Fact]
    public void Launch_NoValidator_ValidSignedFile_LaunchProceeds()
    {
        // Arrange — no validator injected: backward-compat path skips sig check entirely.
        // Existing behaviour must not regress. The file will fail to launch (stub EXE),
        // but that is an EngineError, not a SecurityError.
        var (cacheRoot, filePath) = CreateTempFile();
        try
        {
            var launcher = new DefaultUpdateLauncher(cacheRoot); // no validator

            var result = launcher.Launch(filePath);

            // Path containment OK, file exists: must not be SecurityError.
            if (!result.IsSuccess)
                Assert.NotEqual(ErrorKind.SecurityError, result.Error.Kind);
        }
        finally
        {
            Directory.Delete(cacheRoot, recursive: true);
        }
    }
}
