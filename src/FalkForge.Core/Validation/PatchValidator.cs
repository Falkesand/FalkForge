namespace FalkForge.Validation;

using FalkForge.Models;

public static class PatchValidator
{
    public static ValidationResult Validate(PatchModel model)
    {
        var result = new ValidationResult();

        if (string.IsNullOrWhiteSpace(model.TargetMsiPath))
            result.AddError("MSP001", "Patch TargetMsiPath is required.");

        if (string.IsNullOrWhiteSpace(model.UpdatedMsiPath))
            result.AddError("MSP002", "Patch UpdatedMsiPath is required.");

        if (!Enum.IsDefined(model.Classification))
            result.AddError("MSP003", "Patch Classification is required and must be a valid value.");

        if (model.Id == Guid.Empty)
            result.AddError("MSP004", "Patch Id is required and must be a valid GUID.");

        return result;
    }
}
