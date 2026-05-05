using System.Runtime.Versioning;
using System.Security.Cryptography;
using FalkForge.Compiler.Msi.Cabinets;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Signing;
using FalkForge.Compiler.Msi.Tables;
using FalkForge.Compiler.Msi.Validation;
using FalkForge.Models;
using FalkForge.Platform;
using FalkForge.Platform.Windows;
using FalkForge.Validation;
using FalkForge.WinGet;

namespace FalkForge.Compiler.Msi;

[SupportedOSPlatform("windows")]
public sealed class MsiCompiler : ICompiler
{
    // Retained for public API compatibility — callers that supply a custom IFileSystem
    // will continue to compile successfully. The field is not consumed by the forwarder
    // body (MsiAuthoring.Compile uses its own WindowsFileSystem internally) and will be
    // wired up properly when the IFileSystem injection point is added to MsiAuthoring
    // in Phase 12 cleanup.
#pragma warning disable S4487 // Unread private members
    private readonly IFileSystem _fileSystem;
#pragma warning restore S4487

    public MsiCompiler() : this(new WindowsFileSystem())
    {
    }

    public MsiCompiler(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    /// <summary>
    /// Compiles <paramref name="package"/> to an MSI file under <paramref name="outputPath"/>.
    /// Forwards to <see cref="MsiAuthoring.Compile"/> — the recipe-driven pipeline
    /// (<see cref="MsiRecipeBuilder"/>, <see cref="MsiDatabaseRecipe"/>, <c>IMultiTableProducer</c>
    /// implementations under <c>Recipe/Producers/</c>) that replaced the legacy emitter path in Phase 9.
    /// </summary>
    public Result<string> Compile(PackageModel package, string outputPath)
        => MsiAuthoring.Compile(package, outputPath);
}