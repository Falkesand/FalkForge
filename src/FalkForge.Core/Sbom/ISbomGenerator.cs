namespace FalkForge.Sbom;

public interface ISbomGenerator
{
    Result<Unit> Generate(SbomDocument document, Stream output);
}
