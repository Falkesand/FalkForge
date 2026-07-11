namespace FalkForge.Extensions.Util.UserManagement;

public static class UserValidator
{
    // Characters Windows forbids in SAM account/group names, plus the reserved control range. These names
    // reach a deferred custom action that runs as SYSTEM, so an invalid name is rejected loudly at author
    // time rather than being escaped and silently mangled — defence-in-depth on top of the single-quote
    // escaping the command factory applies.
    private const string ForbiddenNameChars = "\"/\\[]:;|=,+*?<>@";

    /// <summary>
    ///     Validates user creation parameters (legacy 3-argument form: local account, no secure property).
    ///     USR001: Name is required.
    ///     USR002: Password is required for new user creation (when UpdateIfExists is false).
    ///     USR003: Name contains characters not allowed in a Windows account name.
    /// </summary>
    public static Result<bool> Validate(string name, string? password, bool updateIfExists)
        => Validate(name, password, passwordProperty: null, domain: null, updateIfExists);

    /// <summary>
    ///     Validates user creation parameters.
    ///     USR001: Name is required.
    ///     USR002: A credential (literal password or PasswordProperty) is required to create a new LOCAL
    ///             user (domain-qualified users are references, not created, so they are exempt).
    ///     USR003: Name contains characters not allowed in a Windows account name.
    ///     USR011: Password and PasswordProperty are mutually exclusive.
    ///     USR012: A literal password contains a double-quote or control character (unsupported by the
    ///             command-line credential transport).
    /// </summary>
    public static Result<bool> Validate(
        string name, string? password, string? passwordProperty, string? domain, bool updateIfExists)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result<bool>.Failure(ErrorKind.Validation, "USR001: User Name is required.");

        if (!IsValidAccountName(name))
            return Result<bool>.Failure(
                ErrorKind.Validation,
                $"USR003: User Name '{name}' contains characters not allowed in a Windows account name.");

        if (!string.IsNullOrEmpty(password) && !string.IsNullOrEmpty(passwordProperty))
            return Result<bool>.Failure(
                ErrorKind.Validation,
                "USR011: Password and PasswordProperty are mutually exclusive; choose one.");

        // The password rides a double-quoted command-line argument to the deferred action; a literal that
        // contains a double quote (or a control character) would be truncated/mangled at run time, silently
        // giving a privileged account the wrong credential. Reject it at author time.
        if (!string.IsNullOrEmpty(password) && ContainsUnsupportedPasswordChar(password!))
            return Result<bool>.Failure(
                ErrorKind.Validation,
                "USR012: A literal password must not contain a double-quote or control character. " +
                "Use PasswordProperty (SetSecureProperty) for such a value.");

        bool isLocal = string.IsNullOrWhiteSpace(domain);
        bool hasCredential = !string.IsNullOrWhiteSpace(password) || !string.IsNullOrWhiteSpace(passwordProperty);
        if (isLocal && !updateIfExists && !hasCredential)
            return Result<bool>.Failure(
                ErrorKind.Validation,
                "USR002: Password (or PasswordProperty) is required when creating a new local user.");

        return true;
    }

    /// <summary>
    /// True when <paramref name="name"/> is a valid Windows account/group name: non-empty, free of the
    /// forbidden SAM characters and control characters, and not composed solely of dots/spaces.
    /// </summary>
    public static bool IsValidAccountName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        foreach (char c in name)
        {
            if (char.IsControl(c) || ForbiddenNameChars.Contains(c, StringComparison.Ordinal))
                return false;
        }

        // A name of only dots and spaces is rejected by Windows.
        foreach (char c in name)
        {
            if (c is not ('.' or ' '))
                return true;
        }

        return false;
    }

    private static bool ContainsUnsupportedPasswordChar(string password)
    {
        foreach (char c in password)
        {
            if (c == '"' || char.IsControl(c))
                return true;
        }

        return false;
    }
}
