namespace FalkForge.Extensibility;

public interface IFalkForgeExtension
{
    string Name { get; }
    void Register(IExtensionRegistry registry);
}
