using FalkForge.Extensions.Iis.Models;

namespace FalkForge.Extensions.Iis.Builders;

public sealed class AppPoolBuilder
{
    private bool _enable32Bit;
    private string _id = string.Empty;
    private AppPoolIdentityType _identityType = AppPoolIdentityType.ApplicationPoolIdentity;
    private int _idleTimeoutMinutes = 20;
    private string _managedRuntimeVersion = "v4.0";
    private int _maxProcesses = 1;
    private string _name = string.Empty;
    private string? _password;
    private ManagedPipelineMode _pipelineMode = ManagedPipelineMode.Integrated;
    private int _recycleMinutes = 1740;
    private string? _userName;

    public AppPoolBuilder Id(string id)
    {
        _id = id;
        return this;
    }

    public AppPoolBuilder Name(string name)
    {
        _name = name;
        return this;
    }

    public AppPoolBuilder Runtime(string version)
    {
        _managedRuntimeVersion = version;
        return this;
    }

    public AppPoolBuilder NoManagedCode()
    {
        _managedRuntimeVersion = string.Empty;
        return this;
    }

    public AppPoolBuilder PipelineMode(ManagedPipelineMode mode)
    {
        _pipelineMode = mode;
        return this;
    }

    public AppPoolBuilder Enable32Bit()
    {
        _enable32Bit = true;
        return this;
    }

    public AppPoolBuilder Identity(AppPoolIdentityType type)
    {
        _identityType = type;
        return this;
    }

    public AppPoolBuilder Identity(AppPoolIdentityType type, string userName, string password)
    {
        _identityType = type;
        _userName = userName;
        _password = password;
        return this;
    }

    public AppPoolBuilder MaxProcesses(int count)
    {
        _maxProcesses = count;
        return this;
    }

    public AppPoolBuilder RecycleMinutes(int minutes)
    {
        _recycleMinutes = minutes;
        return this;
    }

    public AppPoolBuilder IdleTimeout(int minutes)
    {
        _idleTimeoutMinutes = minutes;
        return this;
    }

    internal AppPoolModel Build()
    {
        return new AppPoolModel
        {
            Id = string.IsNullOrEmpty(_id) ? _name : _id,
            Name = _name,
            ManagedRuntimeVersion = _managedRuntimeVersion,
            ManagedPipelineMode = _pipelineMode,
            Enable32BitAppOnWin64 = _enable32Bit,
            IdentityType = _identityType,
            UserName = _userName,
            Password = _password,
            MaxProcesses = _maxProcesses,
            RecycleMinutes = _recycleMinutes,
            IdleTimeoutMinutes = _idleTimeoutMinutes
        };
    }
}