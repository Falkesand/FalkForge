namespace FalkForge.Extensions.Util.UserManagement;

public sealed class UserBuilder
{
    private readonly List<string> _groups = [];
    private bool _canNotChangePassword;
    private string? _componentRef;
    private string? _description;
    private bool _disabled;
    private string? _domain;
    private string _name = string.Empty;
    private string? _password;
    private bool _passwordExpired;
    private bool _passwordNeverExpires;
    private string? _passwordProperty;
    private bool _removeOnUninstall;
    private bool _updateIfExists;

    public UserBuilder Name(string name)
    {
        _name = name;
        return this;
    }

    /// <summary>
    /// Supplies a literal account password. <b>Discouraged</b> — the literal is embedded in plaintext in
    /// the compiled MSI (USR010 warning). Prefer <see cref="PasswordProperty"/>. Mutually exclusive with
    /// <see cref="PasswordProperty"/>.
    /// </summary>
    public UserBuilder Password(string password)
    {
        _password = password;
        return this;
    }

    /// <summary>
    /// Names an MSI property whose run-time value (supplied via <c>SetSecureProperty</c>) is used as the
    /// account password. The secret is carried to the deferred custom action through the seam's secure
    /// <c>CustomActionData</c> channel and is never stored in the MSI. Mutually exclusive with
    /// <see cref="Password"/>.
    /// </summary>
    public UserBuilder PasswordProperty(string propertyName)
    {
        _passwordProperty = propertyName;
        return this;
    }

    public UserBuilder Domain(string domain)
    {
        _domain = domain;
        return this;
    }

    public UserBuilder Description(string description)
    {
        _description = description;
        return this;
    }

    public UserBuilder CanNotChangePassword()
    {
        _canNotChangePassword = true;
        return this;
    }

    public UserBuilder Disabled()
    {
        _disabled = true;
        return this;
    }

    public UserBuilder PasswordExpired()
    {
        _passwordExpired = true;
        return this;
    }

    public UserBuilder PasswordNeverExpires()
    {
        _passwordNeverExpires = true;
        return this;
    }

    public UserBuilder RemoveOnUninstall()
    {
        _removeOnUninstall = true;
        return this;
    }

    public UserBuilder UpdateIfExists()
    {
        _updateIfExists = true;
        return this;
    }

    public UserBuilder ComponentRef(string componentRef)
    {
        _componentRef = componentRef;
        return this;
    }

    /// <summary>Adds the user to the named local group on install (and removes it on uninstall).</summary>
    public UserBuilder MemberOf(string groupName)
    {
        _groups.Add(groupName);
        return this;
    }

    internal Result<UserModel> Build()
    {
        var validation = UserValidator.Validate(_name, _password, _passwordProperty, _domain, _updateIfExists);
        if (validation.IsFailure)
            return Result<UserModel>.Failure(validation.Error);

        foreach (string group in _groups)
        {
            if (!UserValidator.IsValidAccountName(group))
                return Result<UserModel>.Failure(
                    ErrorKind.Validation,
                    $"USR003: Group name '{group}' referenced by user '{_name}' is not a valid Windows group name.");
        }

        var model = new UserModel
        {
            Name = _name,
            Password = _password,
            PasswordProperty = _passwordProperty,
            Domain = _domain,
            Description = _description,
            CanNotChangePassword = _canNotChangePassword,
            Disabled = _disabled,
            PasswordExpired = _passwordExpired,
            PasswordNeverExpires = _passwordNeverExpires,
            RemoveOnUninstall = _removeOnUninstall,
            UpdateIfExists = _updateIfExists,
            ComponentRef = _componentRef,
            Groups = _groups.ToArray()
        };

        // USR010: non-blocking warning — a literal password is embedded in plaintext in the MSI. Mirrors
        // the SQL015/IIS012/REG007/CTB011 posture: allowed, but the author is steered to PasswordProperty.
        if (!string.IsNullOrEmpty(model.Password))
            Console.Error.WriteLine(
                "[FalkForge Warning] USR010: A literal user password is embedded in plaintext in the MSI. " +
                "Use PasswordProperty with SetSecureProperty instead.");

        return model;
    }
}
