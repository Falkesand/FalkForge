namespace FalkForge.Core.Tests;

using FalkForge.Models;
using FalkForge.Testing;
using Xunit;

public sealed class ReproducibleBuildOptionsTests
{
    [Fact]
    public void Reproducible_WithExplicitEpoch_SetsOptions()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.Reproducible(1708600000L);
        });

        Assert.NotNull(package.ReproducibleOptions);
        Assert.Equal(1708600000L, package.ReproducibleOptions.SourceDateEpoch);
    }

    [Fact]
    public void Reproducible_WithEnvironmentVariable_SetsOptions()
    {
        Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", "1708600000");
        try
        {
            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "App";
                p.Manufacturer = "Corp";
                p.Reproducible();
            });

            Assert.NotNull(package.ReproducibleOptions);
            Assert.Equal(1708600000L, package.ReproducibleOptions.SourceDateEpoch);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", null);
        }
    }

    [Fact]
    public void Reproducible_InvalidEpoch_ThrowsRpr001()
    {
        Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", "not-a-number");
        try
        {
            Assert.Throws<ArgumentException>(() =>
                InstallerTestHost.BuildPackage(p =>
                {
                    p.Name = "App";
                    p.Manufacturer = "Corp";
                    p.Reproducible();
                }));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", null);
        }
    }

    [Fact]
    public void Reproducible_NoEpochAndNoEnvVar_ThrowsRpr002()
    {
        Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", null);

        Assert.Throws<InvalidOperationException>(() =>
            InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "App";
                p.Manufacturer = "Corp";
                p.Reproducible();
            }));
    }

    [Fact]
    public void Timestamp_ReturnsCorrectUtcDateTime()
    {
        var options = new ReproducibleBuildOptions { SourceDateEpoch = 0L };

        Assert.Equal(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc), options.Timestamp);
    }
}
