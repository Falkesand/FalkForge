using FalkForge.Compiler.Msi.UI;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI;

public sealed class MsiControlEventTests
{
    [Theory]
    [InlineData("NewDialog")]
    [InlineData("SpawnDialog")]
    [InlineData("EndDialog")]
    [InlineData("DoAction")]
    [InlineData("AddLocal")]
    [InlineData("AddSource")]
    [InlineData("Remove")]
    [InlineData("Reset")]
    [InlineData("SelectionBrowse")]
    [InlineData("DirectoryListUp")]
    [InlineData("DirectoryListNew")]
    [InlineData("DirectoryListOpen")]
    public void StandardEvent_ValueMatchesMsiVerbatimName(string expected)
    {
        // WHY: Value is written verbatim into the MSI ControlEvent table -- a typo here
        // would silently produce a dialog that does nothing at install time.
        var events = new (string Name, MsiControlEvent Event)[]
        {
            (nameof(MsiControlEvent.NewDialog), MsiControlEvent.NewDialog),
            (nameof(MsiControlEvent.SpawnDialog), MsiControlEvent.SpawnDialog),
            (nameof(MsiControlEvent.EndDialog), MsiControlEvent.EndDialog),
            (nameof(MsiControlEvent.DoAction), MsiControlEvent.DoAction),
            (nameof(MsiControlEvent.AddLocal), MsiControlEvent.AddLocal),
            (nameof(MsiControlEvent.AddSource), MsiControlEvent.AddSource),
            (nameof(MsiControlEvent.Remove), MsiControlEvent.Remove),
            (nameof(MsiControlEvent.Reset), MsiControlEvent.Reset),
            (nameof(MsiControlEvent.SelectionBrowse), MsiControlEvent.SelectionBrowse),
            (nameof(MsiControlEvent.DirectoryListUp), MsiControlEvent.DirectoryListUp),
            (nameof(MsiControlEvent.DirectoryListNew), MsiControlEvent.DirectoryListNew),
            (nameof(MsiControlEvent.DirectoryListOpen), MsiControlEvent.DirectoryListOpen)
        };

        var match = Array.Find(events, e => e.Name == expected);
        Assert.Equal(expected, match.Event.Value);
        Assert.Equal(expected, match.Event.ToString());
    }

    [Fact]
    public void SetProperty_WrapsPropertyNameInBrackets()
    {
        var evt = MsiControlEvent.SetProperty("INSTALLDIR");

        Assert.Equal("[INSTALLDIR]", evt.Value);
        Assert.Equal("[INSTALLDIR]", evt.ToString());
    }

    [Fact]
    public void Parse_NonEmptyValue_ReturnsEventWithThatValue()
    {
        var evt = MsiControlEvent.Parse("CustomActionName");

        Assert.Equal("CustomActionName", evt.Value);
    }

    [Fact]
    public void Parse_EmptyString_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => MsiControlEvent.Parse(""));
    }

    [Fact]
    public void Parse_Null_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => MsiControlEvent.Parse(null!));
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        // record struct value equality -- two independently-parsed events with the
        // same raw string must compare equal, since callers dedupe/compare events.
        var a = MsiControlEvent.Parse("SomeCustomAction");
        var b = MsiControlEvent.Parse("SomeCustomAction");

        Assert.Equal(a, b);
    }

    [Fact]
    public void Equality_DifferentValue_AreNotEqual()
    {
        Assert.NotEqual(MsiControlEvent.NewDialog, MsiControlEvent.SpawnDialog);
    }
}
