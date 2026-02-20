namespace FalkForge.Ui.Abstractions;

/// <summary>
/// Encrypts and decrypts sensitive data (e.g. passwords) while at rest in memory.
/// </summary>
public interface ISensitiveDataProtector
{
    byte[] Protect(byte[] plainData);
    byte[] Unprotect(byte[] protectedData);
}
