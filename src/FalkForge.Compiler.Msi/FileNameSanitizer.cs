namespace FalkForge.Compiler.Msi;

internal static class FileNameSanitizer
{
    internal static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new char[name.Length];
        for (var i = 0; i < name.Length; i++)
        {
            sanitized[i] = Array.IndexOf(invalid, name[i]) >= 0 ? '_' : name[i];
        }
        return new string(sanitized).Replace(' ', '_');
    }
}
