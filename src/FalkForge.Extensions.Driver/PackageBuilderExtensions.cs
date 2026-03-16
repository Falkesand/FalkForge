using FalkForge.Builders;

namespace FalkForge.Extensions.Driver;

public static class PackageBuilderExtensions
{
    public static PackageBuilder Driver(this PackageBuilder builder, Action<DriverBuilder> configure)
    {
        var driverBuilder = new DriverBuilder();
        configure(driverBuilder);
        var result = driverBuilder.Build();
        if (result.IsFailure)
            throw new InvalidOperationException(
                $"Driver configuration error: {result.Error.Message}");

        return builder;
    }
}
