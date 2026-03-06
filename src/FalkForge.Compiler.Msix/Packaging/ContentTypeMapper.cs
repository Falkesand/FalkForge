namespace FalkForge.Compiler.Msix.Packaging;

public static class ContentTypeMapper
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        [".exe"] = "application/x-msdownload",
        [".dll"] = "application/x-msdownload",
        [".xml"] = "application/xml",
        [".json"] = "application/json",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".ico"] = "image/x-icon",
        [".txt"] = "text/plain",
        [".config"] = "application/xml",
        [".dat"] = "application/octet-stream",
        [".html"] = "text/html",
        [".css"] = "text/css",
        [".js"] = "application/javascript",
    };

    public static string GetContentType(string fileName)
        => Map.TryGetValue(Path.GetExtension(fileName), out var ct) ? ct : "application/octet-stream";
}
