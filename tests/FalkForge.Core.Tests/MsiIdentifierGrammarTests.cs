using Xunit;

namespace FalkForge.Tests;

/// <summary>
/// Tests for the shared <see cref="MsiIdentifierGrammar"/> utility in Core — the single
/// canonical MSI-SQL identifier grammar used by both the WRITE side (FalkForge.Compiler.Msi's
/// TableId, which additionally layers a 31-character length cap) and the READ side
/// (FalkForge.Decompiler's MsiTableAccess and FalkForge.Studio's MsiTableReader, which
/// additionally allow dots to tolerate real-world/third-party-authored identifiers).
/// </summary>
public sealed class MsiIdentifierGrammarTests
{
    // Escaped rather than a raw embedded byte so the hostile control character is visible in
    // diffs and cannot be silently normalized away by an editor/encoding tool into a valid
    // identifier.
    private const string ControlCharIdentifier = "Bad\u0001Table";

    // ── IsValidForWrite: base grammar, no dots ───────────────────────────────────

    [Theory]
    [InlineData("Property")]
    [InlineData("_Tables")]
    [InlineData("Component2")]
    public void IsValidForWrite_LegitimateIdentifier_ReturnsTrue(string identifier)
    {
        Assert.True(MsiIdentifierGrammar.IsValidForWrite(identifier));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1Property")]
    [InlineData("My Table")]
    [InlineData("My-Table")]
    [InlineData("Property; DROP TABLE Component;")]
    public void IsValidForWrite_MalformedIdentifier_ReturnsFalse(string? identifier)
    {
        Assert.False(MsiIdentifierGrammar.IsValidForWrite(identifier));
    }

    [Fact]
    public void IsValidForWrite_DottedIdentifier_ReturnsFalse()
    {
        // The write side never authors dotted table names -- see TableId. Dots are a
        // READ-side-only relaxation (see IsValidForRead below).
        Assert.False(MsiIdentifierGrammar.IsValidForWrite("Feature.Main"));
    }

    [Fact]
    public void IsValidForWrite_TrailingNewline_ReturnsFalse()
    {
        // .NET regex `$` matches end-of-string OR immediately before a single trailing '\n',
        // even without RegexOptions.Multiline. \A/\z close that hole.
        Assert.False(MsiIdentifierGrammar.IsValidForWrite("Property\n"));
    }

    // ── IsValidForRead: base grammar plus dots ───────────────────────────────────

    [Theory]
    [InlineData("_Validation")]
    [InlineData("_Columns")]
    [InlineData("_Tables")]
    [InlineData("_Streams")]
    [InlineData("_Storages")]
    [InlineData("MsiFileHash")]
    [InlineData("Property")]
    [InlineData("InstallExecuteSequence")]
    public void IsValidForRead_LegitimateMsiIdentifier_ReturnsTrue(string identifier)
    {
        Assert.True(MsiIdentifierGrammar.IsValidForRead(identifier));
    }

    [Fact]
    public void IsValidForRead_DottedIdentifier_ReturnsTrue()
    {
        // Real Windows Installer identifier grammar permits dots (e.g. dotted
        // CustomAction/Feature-style names). The read side must tolerate them even though
        // FalkForge itself never authors them.
        Assert.True(MsiIdentifierGrammar.IsValidForRead("Feature.Main"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Bad`Table")]
    [InlineData("Bad;Table")]
    [InlineData("Bad'Table")]
    [InlineData("Bad\"Table")]
    [InlineData("Bad Table")]
    [InlineData("Bad%Table")]
    [InlineData("Bad=Table")]
    [InlineData("Bad(Table")]
    [InlineData("Bad)Table")]
    [InlineData("Bad-Table")]
    [InlineData("Bad?Table")]
    [InlineData("Bad*Table")]
    [InlineData("1Property")]
    public void IsValidForRead_MalformedIdentifier_ReturnsFalse(string? identifier)
    {
        Assert.False(MsiIdentifierGrammar.IsValidForRead(identifier));
    }

    [Fact]
    public void IsValidForRead_ControlCharacter_ReturnsFalse()
    {
        Assert.False(MsiIdentifierGrammar.IsValidForRead(ControlCharIdentifier));
    }

    [Fact]
    public void IsValidForRead_TrailingNewline_ReturnsFalse()
    {
        Assert.False(MsiIdentifierGrammar.IsValidForRead("Property" + "\n"));
    }
}
