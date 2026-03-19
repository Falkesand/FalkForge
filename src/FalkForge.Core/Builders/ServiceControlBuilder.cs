using FalkForge.Models;

namespace FalkForge.Builders;

public sealed class ServiceControlBuilder
{
    private string? _arguments;
    private string? _componentRef;
    private ServiceControlEvent _events = ServiceControlEvent.None;
    private string _id = string.Empty;
    private string _serviceName = string.Empty;
    private bool _wait = true;

    public ServiceControlBuilder Id(string id)
    {
        _id = id;
        return this;
    }

    public ServiceControlBuilder ServiceName(string serviceName)
    {
        _serviceName = serviceName;
        return this;
    }

    public ServiceControlBuilder StartOnInstall()
    {
        _events |= ServiceControlEvent.StartOnInstall;
        return this;
    }

    public ServiceControlBuilder StopOnInstall()
    {
        _events |= ServiceControlEvent.StopOnInstall;
        return this;
    }

    public ServiceControlBuilder DeleteOnInstall()
    {
        _events |= ServiceControlEvent.DeleteOnInstall;
        return this;
    }

    public ServiceControlBuilder StartOnUninstall()
    {
        _events |= ServiceControlEvent.StartOnUninstall;
        return this;
    }

    public ServiceControlBuilder StopOnUninstall()
    {
        _events |= ServiceControlEvent.StopOnUninstall;
        return this;
    }

    public ServiceControlBuilder DeleteOnUninstall()
    {
        _events |= ServiceControlEvent.DeleteOnUninstall;
        return this;
    }

    public ServiceControlBuilder Wait(bool wait)
    {
        _wait = wait;
        return this;
    }

    public ServiceControlBuilder Arguments(string arguments)
    {
        _arguments = arguments;
        return this;
    }

    public ServiceControlBuilder ComponentRef(string componentRef)
    {
        _componentRef = componentRef;
        return this;
    }

    internal ServiceControlModel Build()
    {
        return new ServiceControlModel
        {
            Id = _id,
            ServiceName = _serviceName,
            Events = _events,
            Wait = _wait,
            Arguments = _arguments,
            ComponentRef = _componentRef
        };
    }
}