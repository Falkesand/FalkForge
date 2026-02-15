using FalkForge.Builders;
using FalkForge.Models;

namespace FalkForge.Testing;

public static class InstallerTestHost
{
    public static PackageModel BuildPackage(Action<PackageBuilder> configure)
    {
        var builder = new PackageBuilder();
        configure(builder);
        return builder.Build();
    }

    public static PackageBuilder CreateBuilder()
    {
        return new PackageBuilder();
    }
}
