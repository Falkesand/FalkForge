namespace FalkForge.Extensions.Util.UserManagement;

public sealed class GroupBuilder
{
    private string? _componentRef;
    private string? _description;
    private string? _domain;
    private string _name = string.Empty;
    private bool _removeOnUninstall;
    private bool _updateIfExists;

    public GroupBuilder Name(string name)
    {
        _name = name;
        return this;
    }

    public GroupBuilder Domain(string domain)
    {
        _domain = domain;
        return this;
    }

    public GroupBuilder Description(string description)
    {
        _description = description;
        return this;
    }

    public GroupBuilder UpdateIfExists()
    {
        _updateIfExists = true;
        return this;
    }

    public GroupBuilder RemoveOnUninstall()
    {
        _removeOnUninstall = true;
        return this;
    }

    public GroupBuilder ComponentRef(string componentRef)
    {
        _componentRef = componentRef;
        return this;
    }

    internal Result<GroupModel> Build()
    {
        if (string.IsNullOrWhiteSpace(_name))
            return Result<GroupModel>.Failure(ErrorKind.Validation, "GRP001: Group Name is required.");

        if (!UserValidator.IsValidAccountName(_name))
            return Result<GroupModel>.Failure(
                ErrorKind.Validation,
                $"GRP002: Group Name '{_name}' contains characters not allowed in a Windows group name.");

        return new GroupModel
        {
            Name = _name,
            Domain = _domain,
            Description = _description,
            UpdateIfExists = _updateIfExists,
            RemoveOnUninstall = _removeOnUninstall,
            ComponentRef = _componentRef
        };
    }
}
