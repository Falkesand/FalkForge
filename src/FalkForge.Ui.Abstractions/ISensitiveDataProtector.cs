namespace FalkForge.Ui.Abstractions;

/// <summary>
/// Encrypts and decrypts sensitive data (e.g. passwords) while at rest in memory.
/// Implementations should use a session-scoped key that is destroyed on dispose.
/// </summary>
public interface ISensitiveDataProtector
{
    byte[] Protect(byte[] plainData);
    byte[] Unprotect(byte[] protectedData);
}
