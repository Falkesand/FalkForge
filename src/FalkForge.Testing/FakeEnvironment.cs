using FalkForge.Platform;

namespace FalkForge.Testing;

/// <summary>
/// Deterministic <see cref="IEnvironment"/> double for tests that need to prove a specific
/// <see cref="IPlatformServices"/> instance reached production code (as opposed to a null-platform
/// fallback quietly running instead). Modeled on
/// <c>FalkForge.Engine.Tests.Mocks.MockEnvironment</c>; this copy lives in
/// <c>FalkForge.Testing</c> so it can be shared by tests outside the Engine test assembly.
/// </summary>
public sealed class FakeEnvironment : IEnvironment
{
    private readonly Dictionary<string, string> _variables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Environment.SpecialFolder, string> _folders = new();

    public string MachineName { get; set; } = "FAKE-BUILD-HOST";
    public bool Is64BitOperatingSystem { get; set; } = true;

    public FakeEnvironment SetVariable(string name, string value)
    {
        _variables[name] = value;
        return this;
    }

    public FakeEnvironment SetFolderPath(Environment.SpecialFolder folder, string path)
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
