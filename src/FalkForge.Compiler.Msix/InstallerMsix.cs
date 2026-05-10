using System.Diagnostics.CodeAnalysis;
using FalkForge.Compiler.Msix;
using FalkForge.Compiler.Msix.Builders;

namespace FalkForge;

/// <summary>
///     MSIX entry points for the Installer API.
///     Extends the <see cref="Installer" /> surface with BuildMsix and BuildMsixBundle methods.
/// </summary>
public static class InstallerMsix
{
    /// <summary>
    ///     Builds an MSIX package.
    ///     Configures the MSIX model via a fluent builder, validates it,
    ///     and passes the model and output path to the compile function.
    /// </summary>
    /// <param name="args">Command-line arguments (supports -o/--output for output path).</param>
    /// <param name="configure">Action to configure the MSIX builder.</param>
    /// <param name="compile">
    ///     A function that receives the MSIX model and output path,
    ///     and returns the created .msix file path on success.
    /// </param>
    /// <returns>Exit code: 0 for success, 1 for failure.</returns>
    [Experimental("FF_MSIX001", UrlFormat = "https://github.com/falkesand/falkforge/blob/main/docs/experimental/FF_MSIX001.md")]
    public static int BuildMsix(string[] args, Action<MsixBuilder> configure,
        Func<MsixModel, string, Result<string>> compile)
    {
        var builder = new MsixBuilder();
        configure(builder);
        var model = builder.Build();

        var validation = MsixValidator.Validate(model);
        if (validation.IsFailure)
        {
            Console.Error.WriteLine($"MSIX validation failed: {validation.Error}");
            return 1;
        }

        var outputPath = Installer.GetOutputPath(args);
        var result = compile(model, outputPath);
        if (result.IsFailure)
        {
            Console.Error.WriteLine($"MSIX compilation failed: {result.Error}");
            return 1;
        }

        Console.WriteLine($"MSIX package created: {result.Value}");
        return 0;
    }

    /// <summary>
    ///     Builds an MSIX bundle (.msixbundle) containing multiple architecture-specific packages.
    /// </summary>
    /// <param name="args">Command-line arguments (supports -o/--output for output path).</param>
    /// <param name="configure">Action to configure the MSIX bundle builder.</param>
    /// <param name="compile">
    ///     A function that receives the bundle model and output path,
    ///     and returns the created .msixbundle file path on success.
    /// </param>
    /// <returns>Exit code: 0 for success, 1 for failure.</returns>
    [Experimental("FF_MSIX001", UrlFormat = "https://github.com/falkesand/falkforge/blob/main/docs/experimental/FF_MSIX001.md")]
    public static int BuildMsixBundle(string[] args, Action<MsixBundleBuilder> configure,
        Func<MsixBundleModel, string, Result<string>> compile)
    {
        var builder = new MsixBundleBuilder();
        configure(builder);
        var model = builder.Build();

        // MsixBundleModel validation is deferred to the compiler —
        // the bundle format has fewer constraints than individual packages.

        var outputPath = Installer.GetOutputPath(args);
        var result = compile(model, outputPath);
        if (result.IsFailure)
        {
            Console.Error.WriteLine($"MSIX bundle compilation failed: {result.Error}");
            return 1;
        }

        Console.WriteLine($"MSIX bundle created: {result.Value}");
        return 0;
    }
}
