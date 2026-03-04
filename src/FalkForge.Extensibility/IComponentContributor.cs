using FalkForge.Models;

namespace FalkForge.Extensibility;

public interface IComponentContributor
{
    IReadOnlyList<FileEntryModel> GetAdditionalFiles(ExtensionContext context);
}