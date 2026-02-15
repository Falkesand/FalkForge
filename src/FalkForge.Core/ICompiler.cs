namespace FalkForge;

using FalkForge.Models;

public interface ICompiler
{
    Result<string> Compile(PackageModel model, string outputPath);
}
