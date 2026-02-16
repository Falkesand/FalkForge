using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Signing;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

[SupportedOSPlatform("windows")]
public sealed class CodeSignerTests
{
    [Fact]
    public void BuildArguments_WithCertificatePath_IncludesFileFlag()
    {
        var options = new SigningOptions { CertificatePath = @"C:\certs\sign.pfx" };

        var args = CodeSigner.BuildArguments(@"C:\output\app.msi", options);

        Assert.Contains("/f", args);
        Assert.Contains(@"C:\certs\sign.pfx", args);
    }

    [Fact]
    public void BuildArguments_WithThumbprint_IncludesSha1Flag()
    {
        var options = new SigningOptions { CertificateThumbprint = "AABBCCDD" };

        var args = CodeSigner.BuildArguments(@"C:\output\app.msi", options);

        Assert.Contains("/sha1", args);
        Assert.Contains("AABBCCDD", args);
        Assert.Contains("/s", args);
        Assert.Contains("My", args);
    }

    [Fact]
    public void BuildArguments_WithTimestamp_IncludesTrFlag()
    {
        var options = new SigningOptions
        {
            CertificateThumbprint = "AABB",
            TimestampUrl = "http://timestamp.example.com"
        };

        var args = CodeSigner.BuildArguments(@"C:\output\app.msi", options);

        Assert.Contains("/tr", args);
        Assert.Contains("http://timestamp.example.com", args);
        Assert.Contains("/td", args);
    }

    [Fact]
    public void BuildArguments_WithDescription_IncludesDescriptionFlags()
    {
        var options = new SigningOptions
        {
            CertificateThumbprint = "AABB",
            Description = "My Application",
            DescriptionUrl = "https://example.com"
        };

        var args = CodeSigner.BuildArguments(@"C:\output\app.msi", options);

        Assert.Contains("/d", args);
        Assert.Contains("My Application", args);
        Assert.Contains("/du", args);
        Assert.Contains("https://example.com", args);
    }

    [Fact]
    public void BuildArguments_DefaultAlgorithm_IsSha256()
    {
        var options = new SigningOptions { CertificateThumbprint = "AABB" };

        var args = CodeSigner.BuildArguments(@"C:\output\app.msi", options);

        Assert.Contains("/fd", args);
        Assert.Contains("sha256", args);
    }

    [Fact]
    public void Sign_NonExistentFile_ReturnsFailure()
    {
        var signer = new CodeSigner();
        var options = new SigningOptions { CertificateThumbprint = "AABB" };

        var result = signer.Sign(@"C:\nonexistent\ghost.msi", options);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.FileNotFound, result.Error.Kind);
    }

    [Fact]
    public void FindSignTool_ReturnsNullOrPath()
    {
        // This should not throw, regardless of whether the SDK is installed
        var path = CodeSigner.FindSignTool();

        // Either null (SDK not installed) or a valid path
        if (path is not null)
        {
            Assert.EndsWith("signtool.exe", path, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void BuildArguments_FilePathIsLastArgument()
    {
        var options = new SigningOptions { CertificateThumbprint = "AABB" };

        var args = CodeSigner.BuildArguments(@"C:\output\app.msi", options);

        Assert.Equal(@"C:\output\app.msi", args[^1]);
    }

    [Fact]
    public void BuildArguments_NoQuotesAroundValues()
    {
        var options = new SigningOptions
        {
            CertificatePath = @"C:\certs\sign.pfx",
            Description = "My App"
        };

        var args = CodeSigner.BuildArguments(@"C:\output\app.msi", options);

        // ArgumentList handles escaping, so values should not contain wrapping quotes
        Assert.DoesNotContain(args, a => a.StartsWith('"') && a.EndsWith('"'));
    }
}
