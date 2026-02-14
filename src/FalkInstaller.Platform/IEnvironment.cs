namespace FalkInstaller.Platform;

public interface IEnvironment
{
    string MachineName { get; }
    bool Is64BitOperatingSystem { get; }
    string? GetEnvironmentVariable(string name);
    string GetFolderPath(System.Environment.SpecialFolder folder);
}
