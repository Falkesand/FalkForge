using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Signing;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

[SupportedOSPlatform("windows")]
public sealed class CodeSignerMutationTests
{
    [Fact]
    public void BuildArguments_FirstArgIsSign()
    {
        var options = new SigningOptions { CertificateThumbprint = "AA" };
        var args = CodeSigner.BuildArguments("file.msi", options);

        Assert.Equal("sign", args[0]);
    }

    [Fact]
    public void BuildArguments_CertificatePath_FlagAndValueAreAdjacent()
    {
        var options = new SigningOptions { CertificatePath = @"C:\certs\test.pfx" };
        var args = CodeSigner.BuildArguments("file.msi", options);

        var idx = args.IndexOf("/f");
        Assert.True(idx >= 0, "/f flag not found");
        Assert.Equal(@"C:\certs\test.pfx", args[idx + 1]);
    }

    [Fact]
    public void BuildArguments_Thumbprint_FlagAndValueAreAdjacent()
    {
        var options = new SigningOptions { CertificateThumbprint = "DEADBEEF" };
        var args = CodeSigner.BuildArguments("file.msi", options);

        var sha1Idx = args.IndexOf("/sha1");
        Assert.True(sha1Idx >= 0, "/sha1 flag not found");
        Assert.Equal("DEADBEEF", args[sha1Idx + 1]);
    }

    [Fact]
    public void BuildArguments_Thumbprint_IncludesStoreNameFlag()
    {
        var options = new SigningOptions { CertificateThumbprint = "AA" };
        var args = CodeSigner.BuildArguments("file.msi", options);

        var sIdx = args.IndexOf("/s");
        Assert.True(sIdx >= 0, "/s flag not found");
        Assert.Equal("My", args[sIdx + 1]);
    }

    [Fact]
    public void BuildArguments_CertificatePath_DoesNotIncludeThumbprintFlags()
    {
        var options = new SigningOptions { CertificatePath = @"C:\cert.pfx" };
        var args = CodeSigner.BuildArguments("file.msi", options);

        Assert.DoesNotContain("/sha1", args);
        Assert.DoesNotContain("/s", args);
    }

    [Fact]
    public void BuildArguments_DigestAlgorithm_FdFlagIsPresent()
    {
        var options = new SigningOptions { CertificateThumbprint = "AA" };
        var args = CodeSigner.BuildArguments("file.msi", options);

        var fdIdx = args.IndexOf("/fd");
        Assert.True(fdIdx >= 0, "/fd flag not found");
        Assert.Equal("sha256", args[fdIdx + 1]);
    }

    [Fact]
    public void BuildArguments_TimestampUrl_TrAndTdFlagsPresent()
    {
        var options = new SigningOptions
        {
            CertificateThumbprint = "AA",
            TimestampUrl = "http://ts.example.com"
        };
        var args = CodeSigner.BuildArguments("file.msi", options);

        var trIdx = args.IndexOf("/tr");
        Assert.True(trIdx >= 0, "/tr flag not found");
        Assert.Equal("http://ts.example.com", args[trIdx + 1]);

        var tdIdx = args.IndexOf("/td");
        Assert.True(tdIdx >= 0, "/td flag not found");
        Assert.Equal("sha256", args[tdIdx + 1]);
    }

    [Fact]
    public void BuildArguments_NoTimestamp_NoTrOrTdFlags()
    {
        var options = new SigningOptions { CertificateThumbprint = "AA" };
        var args = CodeSigner.BuildArguments("file.msi", options);

        Assert.DoesNotContain("/tr", args);
        Assert.DoesNotContain("/td", args);
    }

    [Fact]
    public void BuildArguments_Description_DFlagAndValuePresent()
    {
        var options = new SigningOptions
        {
            CertificateThumbprint = "AA",
            Description = "My App"
        };
        var args = CodeSigner.BuildArguments("file.msi", options);

        var dIdx = args.IndexOf("/d");
        Assert.True(dIdx >= 0, "/d flag not found");
        Assert.Equal("My App", args[dIdx + 1]);
    }

    [Fact]
    public void BuildArguments_NoDescription_NoDFlag()
    {
        var options = new SigningOptions { CertificateThumbprint = "AA" };
        var args = CodeSigner.BuildArguments("file.msi", options);

        Assert.DoesNotContain("/d", args);
    }

    [Fact]
    public void BuildArguments_DescriptionUrl_DuFlagAndValuePresent()
    {
        var options = new SigningOptions
        {
            CertificateThumbprint = "AA",
            DescriptionUrl = "https://example.com"
        };
        var args = CodeSigner.BuildArguments("file.msi", options);

        var duIdx = args.IndexOf("/du");
        Assert.True(duIdx >= 0, "/du flag not found");
        Assert.Equal("https://example.com", args[duIdx + 1]);
    }

    [Fact]
    public void BuildArguments_NoDescriptionUrl_NoDuFlag()
    {
        var options = new SigningOptions { CertificateThumbprint = "AA" };
        var args = CodeSigner.BuildArguments("file.msi", options);

        Assert.DoesNotContain("/du", args);
    }

    [Fact]
    public void BuildArguments_FilePath_IsAlwaysLastArgument()
    {
        var options = new SigningOptions
        {
            CertificateThumbprint = "AA",
            TimestampUrl = "http://ts.example.com",
            Description = "Desc",
            DescriptionUrl = "http://desc.example.com"
        };
        var args = CodeSigner.BuildArguments(@"C:\out\product.msi", options);

        Assert.Equal(@"C:\out\product.msi", args[^1]);
    }

    [Fact]
    public void BuildArguments_CertificatePath_TakesPriorityOverThumbprint()
    {
        // When both are set, CertificatePath should be used (if/else if structure)
        var options = new SigningOptions
        {
            CertificatePath = @"C:\cert.pfx",
            CertificateThumbprint = "IGNORED"
        };
        var args = CodeSigner.BuildArguments("file.msi", options);

        Assert.Contains("/f", args);
        Assert.DoesNotContain("/sha1", args);
    }

    [Fact]
    public void BuildArguments_AllOptions_ContainsAllFlags()
    {
        var options = new SigningOptions
        {
            CertificatePath = @"C:\cert.pfx",
            TimestampUrl = "http://ts.com",
            Description = "My App",
            DescriptionUrl = "http://app.com"
        };
        var args = CodeSigner.BuildArguments(@"C:\app.msi", options);

        Assert.Equal("sign", args[0]);
        Assert.Contains("/f", args);
        Assert.Contains("/fd", args);
        Assert.Contains("/tr", args);
        Assert.Contains("/td", args);
        Assert.Contains("/d", args);
        Assert.Contains("/du", args);
        Assert.Equal(@"C:\app.msi", args[^1]);
    }

    [Fact]
    public void Sign_NonExistentFile_ReturnsFileNotFoundError()
    {
        var signer = new CodeSigner();
        var options = new SigningOptions { CertificateThumbprint = "AA" };

        var result = signer.Sign(@"C:\definitely\not\real\file.msi", options);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.FileNotFound, result.Error.Kind);
        Assert.Contains("not found", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildArguments_MinimalOptions_ContainsSignFdAndFile()
    {
        // No certificate, no timestamp, no description
        var options = new SigningOptions();
        var args = CodeSigner.BuildArguments("file.msi", options);

        // Should at least have "sign", "/fd", algorithm, and file
        Assert.Equal("sign", args[0]);
        Assert.Contains("/fd", args);
        Assert.Equal("file.msi", args[^1]);
        // No /f, /sha1, /tr, /td, /d, /du
        Assert.DoesNotContain("/f", args);
        Assert.DoesNotContain("/sha1", args);
    }
}
