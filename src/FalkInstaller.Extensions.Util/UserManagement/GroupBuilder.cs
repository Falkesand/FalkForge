namespace FalkInstaller.Extensions.Util.UserManagement;

public sealed class GroupBuilder
{
    private string _name = string.Empty;
    private string? _domain;
    private string? _componentRef;

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

    public GroupBuilder ComponentRef(string componentRef)
    {
        _componentRef = componentRef;
        return this;
    }

    internal Result<GroupModel> Build()
    {
        if (string.IsNullOrWhiteSpace(_name))
            return Result<GroupModel>.Failure(ErrorKind.Validation, "GRP001: Group Name is required.");

        return new GroupModel
        {
            Name = _name,
            Domain = _domain,
            ComponentRef = _componentRef
        };
    }
}
