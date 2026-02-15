namespace FalkForge.Extensions.Util.UserManagement;

public static class UserValidator
{
    /// <summary>
    /// Validates user creation parameters.
    /// USR001: Name is required.
    /// USR002: Password is required for new user creation (when UpdateIfExists is false).
    /// </summary>
    public static Result<bool> Validate(string name, string? password, bool updateIfExists)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result<bool>.Failure(ErrorKind.Validation, "USR001: User Name is required.");

        if (!updateIfExists && string.IsNullOrWhiteSpace(password))
            return Result<bool>.Failure(ErrorKind.Validation, "USR002: Password is required when creating a new user.");

        return true;
    }
}
