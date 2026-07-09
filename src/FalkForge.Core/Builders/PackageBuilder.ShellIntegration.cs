namespace FalkForge.Builders;

// Shell/OS integration: fonts, INI files, permissions, file associations, and COM registration.
public sealed partial class PackageBuilder
{
    public PackageBuilder Font(string fileName, Action<FontBuilder>? configure = null)
    {
        var builder = new FontBuilder(fileName);
        configure?.Invoke(builder);
        _fonts.Add(builder.Build());
        return this;
    }

    public PackageBuilder IniFile(string fileName, Action<IniFileBuilder> configure)
    {
        var builder = new IniFileBuilder(fileName);
        configure(builder);
        _iniFiles.Add(builder.Build());
        return this;
    }

    public PackageBuilder Permission(string lockObject, Action<PermissionBuilder> configure)
    {
        var builder = new PermissionBuilder(lockObject);
        configure(builder);
        _permissions.Add(builder.Build());
        return this;
    }

    public PackageBuilder FileAssociation(string extension, Action<FileAssociationBuilder> configure)
    {
        var builder = new FileAssociationBuilder(extension);
        configure(builder);
        _fileAssociations.Add(builder.Build());
        return this;
    }

    public PackageBuilder ComClass(Action<ComClassBuilder> configure)
    {
        var builder = new ComClassBuilder();
        configure(builder);
        _comClasses.Add(builder.Build());
        return this;
    }

    public PackageBuilder TypeLib(Action<ComTypeLibBuilder> configure)
    {
        var builder = new ComTypeLibBuilder();
        configure(builder);
        _typeLibs.Add(builder.Build());
        return this;
    }
}
