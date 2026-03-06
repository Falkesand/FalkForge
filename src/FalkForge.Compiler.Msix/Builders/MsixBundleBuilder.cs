using FalkForge.Builders;
using FalkForge.Models;

namespace FalkForge.Compiler.Msix.Builders;

public sealed class MsixBundleBuilder
{
    private string _name = string.Empty;
    private string _publisher = string.Empty;
    private Version _version = new(1, 0, 0, 0);
    private readonly List<MsixBundlePackage> _packages = [];
    private SigningOptions? _signing;

    public MsixBundleBuilder Name(string name)
    {
        _name = name;
        return this;
    }

    public MsixBundleBuilder Publisher(string publisher)
    {
        _publisher = publisher;
        return this;
    }

    public MsixBundleBuilder Version(Version version)
    {
        _version = version;
        return this;
    }

    public MsixBundleBuilder Package(string filePath, ProcessorArchitecture arch)
    {
        _packages.Add(new MsixBundlePackage { FilePath = filePath, Architecture = arch });
        return this;
    }

    public MsixBundleBuilder Signing(Action<SigningOptionsBuilder> configure)
    {
        var builder = new SigningOptionsBuilder();
        configure(builder);
        _signing = builder.Build();
        return this;
    }

    public MsixBundleModel Build() => new()
    {
        Name = _name,
        Publisher = _publisher,
        Version = _version,
        Packages = [.. _packages],
        Signing = _signing
    };
}
