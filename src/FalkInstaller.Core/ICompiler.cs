namespace FalkInstaller;

using FalkInstaller.Models;

public interface ICompiler
{
    Result<string> Compile(PackageModel model, string outputPath);
}
