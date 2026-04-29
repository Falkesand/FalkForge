using FalkForge.Compiler.Msi.UI.Layout;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout;

public sealed class DialogButtonNamesTests
{
    [Theory]
    [InlineData(DialogButton.Next, "Next")]
    [InlineData(DialogButton.Back, "Back")]
    [InlineData(DialogButton.Cancel, "Cancel")]
    [InlineData(DialogButton.Install, "Install")]
    [InlineData(DialogButton.Finish, "Finish")]
    [InlineData(DialogButton.Browse, "ChangeFolder")]
    [InlineData(DialogButton.Print, "Print")]
    [InlineData(DialogButton.Remove, "Remove")]
    [InlineData(DialogButton.Repair, "Repair")]
    public void Map_returns_canonical_msi_control_name_for_button(DialogButton button, string expected)
    {
        Assert.True(DialogButtonNames.Map.TryGetValue(button, out var name));
        Assert.Equal(expected, name);
    }

    [Fact]
    public void Map_covers_every_DialogButton_value()
    {
        foreach (DialogButton b in System.Enum.GetValues<DialogButton>())
        {
            Assert.True(DialogButtonNames.Map.ContainsKey(b), $"Missing mapping for {b}");
        }
    }
}
