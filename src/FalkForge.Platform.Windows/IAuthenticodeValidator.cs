namespace FalkForge.Platform.Windows;

public interface IAuthenticodeValidator
{
    Result<Unit> ValidateSignature(string filePath, string? expectedThumbprint);
}
