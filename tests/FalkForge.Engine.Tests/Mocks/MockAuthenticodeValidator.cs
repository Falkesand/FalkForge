namespace FalkForge.Engine.Tests.Mocks;

using FalkForge;
using FalkForge.Platform.Windows;

public sealed class MockAuthenticodeValidator : IAuthenticodeValidator
{
    private Result<Unit>? _result;
    public string? LastFilePath { get; private set; }
    public string? LastThumbprint { get; private set; }
    public string? LastPublicKeyHash { get; private set; }
    public int CallCount { get; private set; }

    public MockAuthenticodeValidator ReturnsSuccess()
    {
        _result = Unit.Value;
        return this;
    }

    public MockAuthenticodeValidator ReturnsFailure(string message)
    {
        _result = Result<Unit>.Failure(ErrorKind.SecurityError, message);
        return this;
    }

    public Result<Unit> ValidateSignature(string filePath, string? expectedThumbprint, string? expectedPublicKeyHash)
    {
        LastFilePath = filePath;
        LastThumbprint = expectedThumbprint;
        LastPublicKeyHash = expectedPublicKeyHash;
        CallCount++;
        return _result ?? Unit.Value;
    }
}
