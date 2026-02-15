using FalkInstaller.Builders;
using FalkInstaller.Models;

namespace FalkInstaller.Localization;

public static class PackageBuilderExtensions
{
    public static PackageBuilder Localization(this PackageBuilder builder, Action<LocalizationBuilder> configure)
    {
        var locBuilder = new LocalizationBuilder();
        configure(locBuilder);

        var result = locBuilder.Build();
        if (result.IsFailure)
        {
            throw new InvalidOperationException(
                $"Localization configuration error: {result.Error.Message}");
        }

        var data = result.Value
            .Select(m => new LocalizationData
            {
                Culture = m.Culture,
                Strings = m.Strings
            })
            .ToList();
        builder.SetLocalizationData(data);

        return builder;
    }
}
