using FalkInstaller.Validation;

namespace FalkInstaller.Extensibility;

public interface IExtensionValidator
{
    void Validate(ExtensionContext context, ValidationResult result);
}
