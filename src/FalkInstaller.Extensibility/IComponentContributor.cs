using FalkInstaller.Models;

namespace FalkInstaller.Extensibility;

public interface IComponentContributor
{
    IReadOnlyList<FileEntryModel> GetAdditionalFiles(ExtensionContext context);
}
