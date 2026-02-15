using FalkForge.Validation;

namespace FalkForge.Extensibility;

public interface IExtensionValidator
{
    void Validate(ExtensionContext context, ValidationResult result);
}
