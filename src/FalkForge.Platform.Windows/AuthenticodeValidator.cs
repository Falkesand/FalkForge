namespace FalkForge.Platform.Windows;

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

[SupportedOSPlatform("windows")]
public sealed class AuthenticodeValidator : IAuthenticodeValidator
{
    public Result<Unit> ValidateSignature(string filePath, string? expectedThumbprint, string? expectedPublicKeyHash)
    {
        if (!File.Exists(filePath))
            return Result<Unit>.Failure(ErrorKind.FileNotFound, $"File not found: {filePath}");

        // Step 1: real Authenticode trust verification via WinVerifyTrust. This validates the
        // file hash against the embedded signature AND that the signer chains to a trusted root.
        // X509Certificate.CreateFromSignedFile alone does NEITHER — it only extracts the signer
        // certificate, so a tampered or self-signed file would pass. Trust must be established
        // before we look at any pin.
        var trustResult = VerifyTrust(filePath);
        if (trustResult.IsFailure)
            return trustResult;

        // Step 2 (optional): pin the publisher identity on top of a successful trust check.
        // No pin requested — a valid signature is sufficient.
        if (expectedThumbprint is null && expectedPublicKeyHash is null)
            return Unit.Value;

        try
        {
            // Load the signer certificate exactly once and evaluate every requested pin against it.
#pragma warning disable SYSLIB0057 // CreateFromSignedFile is obsolete
            using var baseCert = X509Certificate.CreateFromSignedFile(filePath);
#pragma warning restore SYSLIB0057
            using var cert = new X509Certificate2(baseCert);

            if (expectedThumbprint is not null &&
                !string.Equals(cert.Thumbprint, expectedThumbprint, StringComparison.OrdinalIgnoreCase))
            {
                return Result<Unit>.Failure(ErrorKind.SecurityError,
                    $"Certificate thumbprint mismatch. Expected: {expectedThumbprint}, Actual: {cert.Thumbprint}");
            }

            if (expectedPublicKeyHash is not null)
            {
                // Pin the signer's SubjectPublicKeyInfo (SHA-256), not the whole certificate. This
                // ties trust to the publisher's key pair, so a reissued certificate with the same key
                // still matches while a different signer is rejected.
                var spki = cert.PublicKey.ExportSubjectPublicKeyInfo();
                var actualPublicKeyHash = Convert.ToHexString(SHA256.HashData(spki));
                if (!string.Equals(actualPublicKeyHash, expectedPublicKeyHash, StringComparison.OrdinalIgnoreCase))
                {
                    // Do not echo the actual key hash into the failure — the pinned value the author
                    // supplied is what they need to reconcile, and leaking the observed signer key adds
                    // nothing but noise to the security log.
                    return Result<Unit>.Failure(ErrorKind.SecurityError,
                        $"Certificate public-key pin mismatch for '{filePath}': signer public key does not match the pinned value.");
                }
            }

            return Unit.Value;
        }
        catch (CryptographicException ex)
        {
            // WinVerifyTrust already passed, so this is unexpected; surface it rather than hide it.
            return Result<Unit>.Failure(ErrorKind.SecurityError,
                $"Failed to read signer certificate from '{filePath}': {ex.Message}");
        }
    }

    private static Result<Unit> VerifyTrust(string filePath)
    {
        var pFilePath = Marshal.StringToHGlobalUni(filePath);
        var pFileInfo = nint.Zero;
        var pData = nint.Zero;
        // Tracks whether WinVerifyTrust(VERIFY) was called so the finally can issue the
        // matching WinVerifyTrust(CLOSE). If the VERIFY call itself throws (e.g. SEHException
        // on a corrupt or specially-crafted file), pData is still allocated and the WinTrust
        // state handle must be released unconditionally.
        var verifyWasCalled = false;
        try
        {
            var fileInfo = new NativeMethods.WINTRUST_FILE_INFO
            {
                cbStruct = (uint)Marshal.SizeOf<NativeMethods.WINTRUST_FILE_INFO>(),
                pcwszFilePath = pFilePath,
                hFile = nint.Zero,
                pgKnownSubject = nint.Zero,
            };
            pFileInfo = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.WINTRUST_FILE_INFO>());
            Marshal.StructureToPtr(fileInfo, pFileInfo, fDeleteOld: false);

            var data = new NativeMethods.WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<NativeMethods.WINTRUST_DATA>(),
                dwUIChoice = NativeMethods.WTD_UI_NONE,
                // WTD_REVOKE_NONE rather than whole-chain revocation: an installer must work on
                // offline / air-gapped machines where the CRL/OCSP endpoint is unreachable, and a
                // network failure there would otherwise turn into a spurious trust failure. The
                // optional thumbprint pin (step 2) covers the "exact publisher" identity guarantee
                // that revocation would otherwise add.
                fdwRevocationChecks = NativeMethods.WTD_REVOKE_NONE,
                dwUnionChoice = NativeMethods.WTD_CHOICE_FILE,
                pFile = pFileInfo,
                dwStateAction = NativeMethods.WTD_STATE_ACTION_VERIFY,
            };
            pData = Marshal.AllocHGlobal(Marshal.SizeOf<NativeMethods.WINTRUST_DATA>());
            Marshal.StructureToPtr(data, pData, fDeleteOld: false);

            var actionId = NativeMethods.WINTRUST_ACTION_GENERIC_VERIFY_V2;
            verifyWasCalled = true;
            var status = NativeMethods.WinVerifyTrust(nint.Zero, in actionId, pData);

            // 0 = trusted; anything else is the HRESULT-style trust error from WinVerifyTrust.
            if (status == 0)
                return Unit.Value;

            return Result<Unit>.Failure(ErrorKind.SecurityError,
                $"Authenticode verification failed for '{filePath}' (WinVerifyTrust status 0x{status:X8}).");
        }
        finally
        {
            // Release the WinTrust provider state handle unconditionally when pData was
            // allocated and the VERIFY call was attempted (even if it threw). Without this
            // close call, the WinTrust provider leaks its internal state on every exception.
            if (pData != nint.Zero && verifyWasCalled)
            {
                var actionId = NativeMethods.WINTRUST_ACTION_GENERIC_VERIFY_V2;
                var closeData = Marshal.PtrToStructure<NativeMethods.WINTRUST_DATA>(pData);
                closeData.dwStateAction = NativeMethods.WTD_STATE_ACTION_CLOSE;
                Marshal.StructureToPtr(closeData, pData, fDeleteOld: true);
                NativeMethods.WinVerifyTrust(nint.Zero, in actionId, pData);
            }

            if (pData != nint.Zero)
                Marshal.FreeHGlobal(pData);
            if (pFileInfo != nint.Zero)
                Marshal.FreeHGlobal(pFileInfo);
            Marshal.FreeHGlobal(pFilePath);
        }
    }
}
