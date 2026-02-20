namespace FalkForge.Ui;

using System.Security.Cryptography;
using FalkForge.Ui.Abstractions;

public sealed class DpapiDataProtector : ISensitiveDataProtector
{
    public byte[] Protect(byte[] plainData)
        => ProtectedData.Protect(plainData, null, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] protectedData)
        => ProtectedData.Unprotect(protectedData, null, DataProtectionScope.CurrentUser);
}
