using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

public sealed class ComTableEmissionTests
{
    [Theory]
    [InlineData(ComServerType.InprocServer32, "InprocServer32")]
    [InlineData(ComServerType.LocalServer32, "LocalServer32")]
    public void ComClassModel_ContextMapping(ComServerType serverType, string expectedContext)
    {
        var context = serverType == ComServerType.InprocServer32
            ? "InprocServer32"
            : "LocalServer32";

        Assert.Equal(expectedContext, context);
    }

    [Theory]
    [InlineData(1, 0, 256)]
    [InlineData(2, 3, 515)]
    [InlineData(0, 1, 1)]
    [InlineData(255, 255, 65535)]
    public void ComTypeLib_VersionEncoding(int major, int minor, int expectedEncoded)
    {
        var version = new Version(major, minor);
        var encoded = (version.Major << 8) | version.Minor;

        Assert.Equal(expectedEncoded, encoded);
    }
}
