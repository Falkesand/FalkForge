namespace FalkInstaller.Engine.Tests.Mocks;

using FalkInstaller.Platform;

public sealed class MockEnvironment : IEnvironment
{
    private readonly Dictionary<string, string> _variables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Environment.SpecialFolder, string> _folders = new();

    public string MachineName { get; set; } = "TESTMACHINE";
    public bool Is64BitOperatingSystem { get; set; } = true;

    public MockEnvironment SetVariable(string name, string value)
    {
        _variables[name] = value;
        return this;
    }

    public MockEnvironment SetFolderPath(Environment.SpecialFolder folder, string path)
    {
        _folders[folder] = path;
        return this;
    }

    public string? GetEnvironmentVariable(string name)
    {
        return _variables.GetValueOrDefault(name);
    }

    public string GetFolderPath(Environment.SpecialFolder folder)
    {
        return _folders.GetValueOrDefault(folder, string.Empty);
    }
}
