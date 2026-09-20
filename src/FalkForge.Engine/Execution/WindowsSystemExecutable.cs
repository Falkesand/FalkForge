namespace FalkForge.Engine.Execution;

internal static class WindowsSystemExecutable
{
    public static string MsiExec { get; } = Resolve("msiexec.exe");

    public static string Wusa { get; } = Resolve("wusa.exe");

    private static string Resolve(string fileName)
    {
        var systemDirectory = Environment.SystemDirectory;
        if (string.IsNullOrWhiteSpace(systemDirectory) || !Path.IsPathFullyQualified(systemDirectory))
        {
            throw new InvalidOperationException("The Windows system directory could not be resolved safely.");
        }

        return Path.Combine(systemDirectory, fileName);
    }
}
