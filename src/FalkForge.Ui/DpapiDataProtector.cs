using System.Security.Cryptography;
using FalkForge.Ui.Abstractions;

namespace FalkForge.Ui;

public sealed class DpapiDataProtector : ISensitiveDataProtector
{
    public byte[] Protect(byte[] plainData)
    {
        return ProtectedData.Protect(plainData, null, DataProtectionScope.CurrentUser);
    }

    public byte[] Unprotect(byte[] protectedData)
    {
        return ProtectedData.Unprotect(protectedData, null, DataProtectionScope.CurrentUser);
    }
}