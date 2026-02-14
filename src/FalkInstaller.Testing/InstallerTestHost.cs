using FalkInstaller.Builders;
using FalkInstaller.Models;

namespace FalkInstaller.Testing;

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
