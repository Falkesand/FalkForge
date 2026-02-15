namespace FalkForge.Validation;

using FalkForge.Models;

public static class TransformValidator
{
    public static ValidationResult Validate(TransformModel model)
    {
        var result = new ValidationResult();

        if (string.IsNullOrWhiteSpace(model.BaseMsiPath))
            result.AddError("MST001", "Transform BaseMsiPath is required.");

        if (string.IsNullOrWhiteSpace(model.TargetMsiPath))
            result.AddError("MST002", "Transform TargetMsiPath is required.");

        return result;
    }
}
