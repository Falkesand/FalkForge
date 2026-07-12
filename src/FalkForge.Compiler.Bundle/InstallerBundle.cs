using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;

namespace FalkForge;

/// <summary>
///     Bundle entry point for the Installer API.
///     Extends the <see cref="Installer" /> surface with a <see cref="BuildBundle(string[], Action{BundleBuilder}, BundleCompiler?)" />
///     overload that wires <see cref="BundleBuilder" /> and <see cref="BundleCompiler" /> internally — mirroring
///     how <see cref="Installer.Build(string[], Action{FalkForge.Builders.PackageBuilder}, ICompiler?)" /> wires
///     <c>PackageBuilder</c> and <c>MsiCompiler</c> for MSI authoring. Callers who need a bundle compiled by a
///     different/async signing path keep using <see cref="Installer.BuildBundle(string[], Func{string, Result{string}})" />
///     or <see cref="Installer.BuildBundleAsync" /> directly.
/// </summary>
public static class InstallerBundle
{
    /// <summary>
    ///     Builds a bundle installer package.
    ///     Configures the bundle model via a fluent builder, then compiles it with the supplied
    ///     (or a default) <see cref="BundleCompiler" />.
    /// </summary>
    /// <param name="args">Command-line arguments (supports -o/--output for output path).</param>
    /// <param name="configure">Action to configure the bundle builder.</param>
    /// <param name="compiler">
    ///     The compiler to use. Defaults to a plain <see cref="BundleCompiler" /> when omitted; pass a
    ///     pre-configured instance to set <see cref="BundleCompiler.EngineStubPath" />,
    ///     <see cref="BundleCompiler.AllowPlaceholderStub" />, or <see cref="BundleCompiler.ElevationCompanionPath" />.
    /// </param>
    /// <returns>Exit code: 0 for success, 1 for failure.</returns>
    public static int BuildBundle(string[] args, Action<BundleBuilder> configure, BundleCompiler? compiler = null)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new BundleBuilder();
        configure(builder);
        var model = builder.Build();

        var outputPath = Installer.GetOutputPath(args);
        var result = (compiler ?? new BundleCompiler()).Compile(model, outputPath);
        if (result.IsFailure)
        {
            Console.Error.WriteLine($"Bundle compilation failed: {result.Error}");
            return 1;
        }

        Console.WriteLine($"Bundle created: {result.Value}");
        return 0;
    }
}
