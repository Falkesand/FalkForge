namespace FalkForge.Platform.Windows;

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

public sealed class AuthenticodeValidator : IAuthenticodeValidator
{
    public Result<Unit> ValidateSignature(string filePath, string? expectedThumbprint)
    {
        if (!File.Exists(filePath))
            return Result<Unit>.Failure(ErrorKind.FileNotFound, $"File not found: {filePath}");

        try
        {
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
            return Unit.Value;
        }
        catch (CryptographicException)
        {
            return Result<Unit>.Failure(ErrorKind.SecurityError, $"File is not signed or has invalid signature: {filePath}");
        }
    }
}
