namespace FalkInstaller.Builders;

using FalkInstaller.Models;

public sealed class RemoveRegistryBuilder
{
    private string _id = string.Empty;
    private RegistryRoot _root;
    private string _key = string.Empty;
    private string? _name;
    private RemoveRegistryAction _action = RemoveRegistryAction.RemoveKey;
    private string? _componentRef;

    public RemoveRegistryBuilder Id(string id)
    {
        _id = id;
        return this;
    }

    public RemoveRegistryBuilder Root(RegistryRoot root)
    {
        _root = root;
        return this;
    }

    public RemoveRegistryBuilder Key(string key)
    {
        _key = key;
        return this;
    }

    public RemoveRegistryBuilder Name(string name)
    {
        _name = name;
        return this;
    }

    public RemoveRegistryBuilder RemoveKey()
    {
        _action = RemoveRegistryAction.RemoveKey;
        return this;
    }

    public RemoveRegistryBuilder RemoveValue()
    {
        _action = RemoveRegistryAction.RemoveValue;
        return this;
    }

    public RemoveRegistryBuilder ComponentRef(string componentRef)
    {
        _componentRef = componentRef;
        return this;
    }

    internal RemoveRegistryModel Build() => new()
    {
        Id = _id,
        Action = _action,
        Root = _root,
        Key = _key,
        Name = _name,
        ComponentRef = _componentRef
    };
}
