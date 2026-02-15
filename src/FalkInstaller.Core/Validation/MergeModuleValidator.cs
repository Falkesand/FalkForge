namespace FalkInstaller.Validation;

using FalkInstaller.Models;

public static class MergeModuleValidator
{
    public static ValidationResult Validate(MergeModuleModel model)
    {
        var result = new ValidationResult();

        if (model.Id == Guid.Empty)
            result.AddError("MSM001", "Merge module Id is required and must be a valid GUID.");

        if (model.Version is null)
            result.AddError("MSM002", "Merge module Version is required.");

        if (model.Language == 0)
            result.AddError("MSM003", "Merge module Language is required (e.g. 1033 for English).");

        if (string.IsNullOrWhiteSpace(model.Manufacturer))
            result.AddError("MSM004", "Merge module Manufacturer is required.");

        return result;
    }
}
