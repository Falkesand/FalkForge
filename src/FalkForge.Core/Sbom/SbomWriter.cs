using System.Text;

namespace FalkForge.Sbom;

public static class SbomWriter
{
    private static readonly CycloneDxSbomGenerator Generator = new();

    public static Result<Unit> WriteToFile(SbomDocument document, string filePath)
    {
        try
        {
            using var stream = File.OpenWrite(filePath);
            return Generator.Generate(document, stream);
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure(ErrorKind.IoError, $"Failed to write SBOM to {filePath}: {ex.Message}");
        }
    }

    public static Result<string> WriteToString(SbomDocument document)
    {
        using var ms = new MemoryStream();
        var result = Generator.Generate(document, ms);
        if (result.IsFailure)
            return Result<string>.Failure(result.Error);

        return Result<string>.Success(Encoding.UTF8.GetString(ms.ToArray()));
    }
}
