using FalkForge.Models;

namespace FalkForge;

public interface ICompiler
{
    Result<string> Compile(PackageModel model, string outputPath);
}