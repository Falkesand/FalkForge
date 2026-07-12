using FalkForge.Compiler.Bundle.Validation;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Validation;

/// <summary>
/// BDL033: a remote-payload certificate public-key pin must be a well-formed SHA-256 public-key
/// hash (64 hex chars). A malformed pin would never match a real signer and would silently turn
/// the security pin into an always-fail gate, so it must fail loud at author time.
/// </summary>
public sealed class RemotePayloadPinValidationTests : IDisposable
{
    // A valid SHA-256 public-key pin: 64 hexadecimal characters.
    private const string ValidPin = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

    private readonly string _tempDir;
    private readonly BundleValidator _validator = new();

    public RemotePayloadPinValidationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"RemotePin_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Validate_MalformedPin_ReturnsBdl033()
    {
        var model = CreateModel("not-a-valid-64-char-hex-pin");

        var result = _validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("BDL033", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_PinWrongLength_ReturnsBdl033()
    {
        // 63 hex chars: correct alphabet, wrong length.
        var model = CreateModel(new string('A', 63));

        var result = _validator.Validate(model);

        Assert.True(result.IsFailure);
        Assert.Contains("BDL033", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ValidPin_Succeeds()
    {
        var model = CreateModel(ValidPin);

        var result = _validator.Validate(model);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_NoPin_Succeeds()
    {
        var model = CreateModel(certificatePublicKey: null);

        var result = _validator.Validate(model);

        Assert.True(result.IsSuccess);
    }

    private BundleModel CreateModel(string? certificatePublicKey)
    {
        var sourceFile = Path.Combine(_tempDir, "remote.msi");
        File.WriteAllText(sourceFile, "content");

        return new BundleModel
        {
            Name = "TestApp",
            Manufacturer = "TestCo",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages =
            [
                new BundlePackageModel
                {
                    Id = "RemoteMsi",
                    Type = BundlePackageType.MsiPackage,
                    DisplayName = "Remote",
                    SourcePath = sourceFile,
                    RemotePayload = new RemotePayloadModel
                    {
                        DownloadUrl = "https://example.com/remote.msi",
                        Sha256Hash = "AABBCCDD",
                        Size = 1024,
                        CertificatePublicKey = certificatePublicKey
                    }
                }
            ]
        };
    }
}
