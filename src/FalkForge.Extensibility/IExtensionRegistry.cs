namespace FalkForge.Extensibility;

public interface IExtensionRegistry
{
    void RegisterTableContributor(IMsiTableContributor contributor);
    void RegisterComponentContributor(IComponentContributor contributor);
    void RegisterValidator(IExtensionValidator validator);
}
