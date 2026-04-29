using System;
using System.Collections.Immutable;
using FalkForge.Compiler.Msi.UI.Layout;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.UI.Layout;

public sealed class DialogComposerTests
{
    private static DialogContent MinimalContent(string name = "WelcomeDlg") => new()
    {
        Name = name,
        Kind = "Welcome",
        Placements = ImmutableArray<RegionPlacement>.Empty,
    };

    [Fact]
    public void Compose_with_null_content_throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => DialogComposer.Compose(null!, Layouts.Standard370x270));
    }

    [Fact]
    public void Compose_with_null_layout_throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => DialogComposer.Compose(MinimalContent(), null!));
    }

    [Fact]
    public void Compose_with_minimal_content_returns_model_with_matching_name()
    {
        var content = MinimalContent("MyDialog");

        var model = DialogComposer.Compose(content, Layouts.Standard370x270);

        Assert.Equal("MyDialog", model.Name);
    }

    [Fact]
    public void Compose_with_minimal_content_returns_empty_controls()
    {
        var model = DialogComposer.Compose(MinimalContent(), Layouts.Standard370x270);

        Assert.Empty(model.Controls);
    }

    [Fact]
    public void Compose_uses_layout_canvas_dimensions()
    {
        var model = DialogComposer.Compose(MinimalContent(), Layouts.Standard370x270);

        Assert.Equal(370, model.Width);
        Assert.Equal(270, model.Height);
    }
}
