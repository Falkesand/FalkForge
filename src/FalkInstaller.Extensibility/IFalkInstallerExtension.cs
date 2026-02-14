namespace FalkInstaller.Extensibility;

public interface IFalkInstallerExtension
{
    string Name { get; }
    void Register(IExtensionRegistry registry);
}
