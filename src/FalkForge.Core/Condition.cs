namespace FalkForge;

public sealed class Condition
{
    private readonly string _expression;

    // Internal constructor - called by MsiProperty comparison operators and internal factories
    internal Condition(string expression)
    {
        _expression = expression;
    }

    // Factory methods
    public static Condition Property(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Condition(name);
    }

    public static Condition Raw(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        return new Condition(expression);
    }

    // Logical operators
    // & -> AND with parenthesization
    public static Condition operator &(Condition left, Condition right) =>
        new($"({left._expression}) AND ({right._expression})");

    // | -> OR with parenthesization
    public static Condition operator |(Condition left, Condition right) =>
        new($"({left._expression}) OR ({right._expression})");

    // ! -> NOT with parenthesization
    public static Condition operator !(Condition condition) =>
        new($"NOT ({condition._expression})");

    // Implicit conversion to string for backward compat with string-accepting methods
    public static implicit operator string(Condition condition) => condition._expression;

    public override string ToString() => _expression;

    // Pre-composed conditions
    public static Condition Is64BitOS { get; } = new("VersionNT64 OR Msix64");
    public static Condition IsPrivileged { get; } = new("Privileged");
    public static Condition IsAdmin { get; } = new("AdminUser");
    public static Condition IsTerminalServer { get; } = new("TerminalServer");
    public static Condition IsWindows10OrLater { get; } = new("VersionNT >= 603");
    public static Condition IsWindows11OrLater { get; } = new("WindowsBuildNumber >= 22000");
    public static Condition IsInstalled { get; } = new("Installed");
    public static Condition IsInstalling { get; } = new("NOT Installed");
    public static Condition IsUninstalling { get; } = new("REMOVE=\"ALL\"");
    public static Condition IsRepairing { get; } = new("REINSTALL");
}
