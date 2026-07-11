using System.Runtime.Versioning;
using System.Security.Cryptography;
using FalkForge.Compiler.Msi.Cabinets;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Signing;
using FalkForge.Compiler.Msi.Tables;
using FalkForge.Compiler.Msi.Validation;
using FalkForge.Diagnostics;
using FalkForge.Extensibility;
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
#pragma warning disable S4487, IDE0052 // Unread private members
    private readonly IFileSystem _fileSystem;
#pragma warning restore S4487, IDE0052

    private readonly List<IFalkForgeExtension> _extensions;
    private readonly IFalkLogger? _logger;

    public MsiCompiler() : this(new WindowsFileSystem())
    {
    }

    public MsiCompiler(IFileSystem fileSystem) : this(fileSystem, [])
    {
    }

    /// <summary>
    /// Initialises the compiler with a custom file system, a set of extensions whose
    /// validation rules are merged into the validation engine and whose registered
    /// <c>IMsiTableContributor</c> rows are emitted into the compiled MSI, and an optional
    /// structured logger. <paramref name="logger"/> defaults to <see langword="null"/>
    /// (no-op) so every existing caller compiles and behaves unchanged.
    /// </summary>
    public MsiCompiler(IFileSystem fileSystem, IReadOnlyList<IFalkForgeExtension> extensions, IFalkLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(extensions);
        _fileSystem = fileSystem;
        _extensions = [.. extensions];
        _logger = logger;
    }

    /// <summary>
    /// Attaches <paramref name="extension"/> to this compiler so its validation rules run and
    /// its registered <c>IMsiTableContributor</c> tables/rows are emitted into the compiled MSI.
    /// This is the discoverable, fluent way to activate an extension:
    /// <code>
    /// var sql = new SqlExtension();
    /// sql.DefineDatabase(db => db.Id("AppDb").Server(".").Database("Demo").CreateOnInstall());
    /// return Installer.Build(args, package => { /* ... */ }, new MsiCompiler().Use(sql));
    /// </code>
    /// The call mutates and returns the same compiler, so a discarded result still attaches the
    /// extension — a newcomer cannot accidentally build an installer that silently omits it.
    /// </summary>
    public MsiCompiler Use(IFalkForgeExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        _extensions.Add(extension);
        return this;
    }

    /// <summary>Attaches every extension in <paramref name="extensions"/>, in order. See <see cref="Use(IFalkForgeExtension)"/>.</summary>
    public MsiCompiler Use(params IFalkForgeExtension[] extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        foreach (IFalkForgeExtension extension in extensions)
        {
            ArgumentNullException.ThrowIfNull(extension);
            _extensions.Add(extension);
        }

        return this;
    }

    /// <summary>
    /// Compiles <paramref name="model"/> to an MSI file under <paramref name="outputPath"/>.
    /// Forwards to <see cref="MsiAuthoring.Compile"/> — the recipe-driven pipeline
    /// (<see cref="MsiRecipeBuilder"/>, <see cref="MsiDatabaseRecipe"/>, <c>IMultiTableProducer</c>
    /// implementations under <c>Recipe/Producers/</c>) that replaced the legacy emitter path in Phase 9.
    /// </summary>
    public Result<string> Compile(PackageModel model, string outputPath)
        => MsiAuthoring.Compile(model, outputPath, _extensions, _logger);
}