using System.Text.Json;

namespace FalkInstaller.Localization;

public static class LocalizationLoader
{
    public static Result<LocalizationModel> LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            return Result<LocalizationModel>.Failure(ErrorKind.FileNotFound, $"Localization file not found: {filePath}");

        var culture = ExtractCultureFromFileName(filePath);
        if (culture is null)
            return Result<LocalizationModel>.Failure(ErrorKind.Validation,
                "LOC004: Cannot extract culture from filename. Expected format: name.culture.json (e.g., strings.en-US.json)");

        string json;
        try
        {
            json = File.ReadAllText(filePath);
        }
        catch (IOException ex)
        {
            return Result<LocalizationModel>.Failure(ErrorKind.IoError, $"Failed to read localization file: {ex.Message}");
        }

        return ParseJson(json, culture);
    }

    internal static Result<LocalizationModel> ParseJson(string json, string culture)
    {
        Dictionary<string, JsonElement>? raw;
        try
        {
            raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
        }
        catch (JsonException ex)
        {
            return Result<LocalizationModel>.Failure(ErrorKind.Validation,
                $"LOC004: Invalid JSON in localization file: {ex.Message}");
        }

        if (raw is null)
            return Result<LocalizationModel>.Failure(ErrorKind.Validation,
                "LOC004: Localization file contains null JSON");

        var strings = new Dictionary<string, string>(raw.Count);
        foreach (var (key, element) in raw)
        {
            if (element.ValueKind != JsonValueKind.String)
                return Result<LocalizationModel>.Failure(ErrorKind.Validation,
                    $"LOC004: Value for key '{key}' is not a string. All localization values must be strings.");

            strings[key] = element.GetString()!;
        }

        return new LocalizationModel
        {
            Culture = culture,
            Strings = strings
        };
    }

    internal static string? ExtractCultureFromFileName(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        // Expected: name.culture where culture is like "en-US", "de", "zh-Hans", "zh-Hans-CN"
        var dotIndex = fileName.IndexOf('.');
        if (dotIndex < 0 || dotIndex == fileName.Length - 1)
            return null;

        var culturePart = fileName[(dotIndex + 1)..];

        // Validate culture format: letters, optionally followed by -letters segments
        if (!IsValidCultureFormat(culturePart))
            return null;

        return culturePart;
    }

    private static bool IsValidCultureFormat(string culture)
    {
        if (string.IsNullOrEmpty(culture))
            return false;

        var segments = culture.Split('-');
        foreach (var segment in segments)
        {
            if (segment.Length == 0)
                return false;

            foreach (var c in segment)
            {
                if (!char.IsLetter(c))
                    return false;
            }
        }

        return true;
    }
}
