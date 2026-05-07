using FalkForge.Validation;
using Xunit;

namespace FalkForge.Core.Tests.Validation;

public sealed class ModelPathTests
{
    [Fact]
    public void Root_produces_empty_string()
    {
        Assert.Equal("", ModelPath.Root.ToString());
    }

    [Fact]
    public void Field_appends_field_name()
    {
        var path = ModelPath.Root.Field("package");

        Assert.Equal("package", path.ToString());
    }

    [Fact]
    public void Index_appends_bracket_index()
    {
        var path = ModelPath.Root.Field("Features").Index(2);

        Assert.Equal("Features[2]", path.ToString());
    }

    [Fact]
    public void Key_appends_bracket_key()
    {
        var path = ModelPath.Root.Field("Properties").Key("AppVersion");

        Assert.Equal("Properties[AppVersion]", path.ToString());
    }

    [Fact]
    public void Chained_path_produces_dotted_string()
    {
        var path = ModelPath.Root
            .Field("Features")
            .Index(2)
            .Field("Services")
            .Index(0)
            .Field("Name");

        Assert.Equal("Features[2].Services[0].Name", path.ToString());
    }

    [Fact]
    public void Two_equal_paths_are_structurally_equal()
    {
        var a = ModelPath.Root.Field("Services").Index(0).Field("Name");
        var b = ModelPath.Root.Field("Services").Index(0).Field("Name");

        Assert.Equal(a, b);
    }

    [Fact]
    public void Different_paths_are_not_equal()
    {
        var a = ModelPath.Root.Field("Services").Index(0).Field("Name");
        var b = ModelPath.Root.Field("Services").Index(1).Field("Name");

        Assert.NotEqual(a, b);
    }
}
