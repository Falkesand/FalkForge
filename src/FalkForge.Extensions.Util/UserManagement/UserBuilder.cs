namespace FalkForge.Extensions.Util.UserManagement;

public sealed class UserBuilder
{
    private bool _canNotChangePassword;
    private string? _componentRef;
    private bool _disabled;
    private string? _domain;
    private string _name = string.Empty;
    private string? _password;
    private bool _passwordExpired;
    private bool _passwordNeverExpires;
    private bool _removeOnUninstall;
    private bool _updateIfExists;

    public UserBuilder Name(string name)
    {
        _name = name;
        return this;
    }

    public UserBuilder Password(string password)
    {
        _password = password;
        return this;
    }

    public UserBuilder Domain(string domain)
    {
        _domain = domain;
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

    internal Result<UserModel> Build()
    {
        var validation = UserValidator.Validate(_name, _password, _updateIfExists);
        if (validation.IsFailure)
            return Result<UserModel>.Failure(validation.Error);

        return new UserModel
        {
            Name = _name,
            Password = _password,
            Domain = _domain,
            CanNotChangePassword = _canNotChangePassword,
            Disabled = _disabled,
            PasswordExpired = _passwordExpired,
            PasswordNeverExpires = _passwordNeverExpires,
            RemoveOnUninstall = _removeOnUninstall,
            UpdateIfExists = _updateIfExists,
            ComponentRef = _componentRef
        };
    }
}