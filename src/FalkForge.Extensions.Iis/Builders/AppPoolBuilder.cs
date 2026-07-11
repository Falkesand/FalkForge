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
    private string? _passwordProperty;
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

    /// <summary>
    /// Configures a <c>SpecificUser</c> identity whose password is supplied securely at run time via the
    /// named MSI property (populated through <c>IInstallerEngine.SetSecureProperty</c>). The password is
    /// never stored in the MSI — the recommended path. Mutually exclusive with the literal-password
    /// <see cref="Identity(AppPoolIdentityType,string,string)"/> overload.
    /// <para>
    /// <b>Runtime exposure (honest limitations).</b> The resolved password reaches the deferred custom
    /// action as <c>CustomActionData</c> and is set on the app-pool <c>ProcessModel.Password</c> in-process
    /// (via <c>Microsoft.Web.Administration</c>), so — unlike an <c>appcmd</c> command line — it is never
    /// placed on a child process command line. The IIS extension adds the carrying properties to
    /// <c>MsiHiddenProperties</c> so their values are redacted from a verbose MSI log. The property name must
    /// be a public (uppercase) MSI property, and the value must not contain a double-quote character (the
    /// EXE custom-action transport is double-quoted).
    /// </para>
    /// </summary>
    public AppPoolBuilder IdentitySecure(AppPoolIdentityType type, string userName, string passwordProperty)
    {
        _identityType = type;
        _userName = userName;
        _passwordProperty = passwordProperty;
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
        var model = new AppPoolModel
        {
            Id = string.IsNullOrEmpty(_id) ? _name : _id,
            Name = _name,
            ManagedRuntimeVersion = _managedRuntimeVersion,
            ManagedPipelineMode = _pipelineMode,
            Enable32BitAppOnWin64 = _enable32Bit,
            IdentityType = _identityType,
            UserName = _userName,
            PasswordProperty = _passwordProperty,
            Password = _password,
            MaxProcesses = _maxProcesses,
            RecycleMinutes = _recycleMinutes,
            IdleTimeoutMinutes = _idleTimeoutMinutes
        };

        // IIS012: non-blocking warning — a literal app-pool password is embedded in plaintext in the MSI.
        // Mirrors the SQL015/REG007/CTB011 posture: allowed, but the author is steered to IdentitySecure
        // (PasswordProperty + SetSecureProperty).
        if (!string.IsNullOrEmpty(_password))
            Console.Error.WriteLine(
                "[FalkForge Warning] IIS012: A literal IIS app-pool password is embedded in plaintext in the MSI. " +
                "Use IdentitySecure(...) with a PasswordProperty populated by SetSecureProperty instead.");

        return model;
    }
}