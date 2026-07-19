using FalkForge.Configuration;
using FalkForge.Models;
using FalkForge.Sbom;

namespace FalkForge.Builders;

// Package-level build configuration: media, restart manager, cabinet threading, signing,
// integrity, dialog set, localization, reproducibility, SBOM, WinGet, and ICE.
public sealed partial class PackageBuilder
{
    public PackageBuilder MediaTemplate(Action<MediaTemplateBuilder> configure)
    {
        var builder = new MediaTemplateBuilder();
        configure(builder);
        _mediaTemplate = builder.Build();
        return this;
    }

    /// <summary>
    /// Sets the product icon shown in Add/Remove Programs (the MSI
    /// <c>ARPPRODUCTICON</c> property). The icon file is embedded in the MSI
    /// <c>Icon</c> table at compile time.
    /// </summary>
    public PackageBuilder ProductIcon(string iconFilePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(iconFilePath);
        ProductIconFile = iconFilePath;
        return this;
    }

    public PackageBuilder EnableRestartManagerSupport()
    {
        EnableRestartManager = true;
        return this;
    }

    public PackageBuilder CabinetThreads(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        CabinetThreadCount = count;
        return this;
    }

    public PackageBuilder Signing(Action<SigningOptionsBuilder> configure)
    {
        var builder = new SigningOptionsBuilder();
        configure(builder);
        _signing = builder.Build();
        return this;
    }

    public PackageBuilder Integrity(Action<IntegrityBuilder> configure)
    {
        var builder = new IntegrityBuilder();
        configure(builder);
        _integrity = builder.Build();
        return this;
    }

    public PackageBuilder UseDialogSet(MsiDialogSet dialogSet)
    {
        _dialogSet = dialogSet;
        return this;
    }

    public PackageBuilder UseDialogSet(MsiDialogSet dialogSet, Action<DialogCustomization> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _dialogSet = dialogSet;
        DialogCustomization customization = new();
        configure(customization);
        _dialogCustomization = customization;
        return this;
    }

    public PackageBuilder SetLocalizationData(IReadOnlyList<LocalizationData> data)
    {
        _localizationData.Clear();
        _localizationData.AddRange(data);
        return this;
    }

    public PackageBuilder Reproducible(long? epochOverride = null)
    {
        long epoch;
        if (epochOverride.HasValue)
        {
            epoch = epochOverride.Value;
        }
        else
        {
            var lookup = EnvVarCatalog.TryGetSourceDateEpoch();
            if (lookup.IsFailure)
                throw new ArgumentException(lookup.Error.Message);
            if (!lookup.Value.IsSet)
                throw new InvalidOperationException(
                    "RPR002: SOURCE_DATE_EPOCH is not set and no explicit epoch was provided.");
            epoch = lookup.Value.Value;
        }

        _reproducibleOptions = new ReproducibleBuildOptions { SourceDateEpoch = epoch };
        return this;
    }

    public PackageBuilder Sbom(Action<SbomOptions>? configure = null)
    {
        _sbomOptions ??= new SbomOptions();
        configure?.Invoke(_sbomOptions);
        return this;
    }

    public PackageBuilder WinGet(Action<WinGetBuilder> configure)
    {
        var builder = new WinGetBuilder();
        configure(builder);
        _winGet = builder.Build();
        return this;
    }

    public PackageBuilder Ice(Action<IceConfigurationBuilder> configure)
    {
        var builder = new IceConfigurationBuilder();
        configure(builder);
        _iceConfiguration = builder.Build();
        return this;
    }
}
