using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using FalkForge;
using FalkForge.Cli.Models;
using FalkForge.Cli.Settings;
using FalkForge.Cli.WinGet;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Compiler.Msi;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Signing;
using FalkForge.Validation;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FalkForge.Cli.Commands;

/// <summary>
/// Compiles an installer definition (.cs, .csx, or .json) into an MSI.
/// Uses Roslyn scripting for .cs/.csx files and JsonConfigLoader for .json files.
/// Dispatch lives in <see cref="BuildInputResolver"/>.
/// The command is asynchronous because a JSON config with a <c>signing</c> section drives the
/// async bundle build path (a remote <see cref="ISignatureProvider"/> performs network I/O);
/// without signing the flow completes synchronously exactly as before.
/// </summary>
public sealed class BuildCommand : AsyncCommand<BuildSettings>
{
    // _console is swapped to a JsonConsoleOutput buffer when settings.Json is set so the
    // entire build run accumulates messages into a single envelope rendered at the end.
    private IConsoleOutput _console;
    private readonly string? _gitWorkingDirectory;
    private readonly System.IO.TextWriter _jsonSink;

    public BuildCommand() : this(new SpectreConsoleOutput()) { }

    public BuildCommand(IConsoleOutput console, string? gitWorkingDirectory = null, System.IO.TextWriter? jsonSink = null)
    {
        _console = console;
        _gitWorkingDirectory = gitWorkingDirectory;
        _jsonSink = jsonSink ?? Console.Out;
    }

    protected override async Task<int> ExecuteAsync([NotNull] CommandContext context, [NotNull] BuildSettings settings, CancellationToken cancellationToken)
    {
        var originalConsole = _console;
        var jsonOutput = settings.Json ? new JsonConsoleOutput() : null;
        if (jsonOutput is not null)
            _console = jsonOutput;

        try
        {
            var exitCode = await ExecuteInternalAsync(settings, cancellationToken).ConfigureAwait(false);
            if (jsonOutput is not null)
            {
                IReadOnlyDictionary<string, string?>? envelopeResult = settings.DryRun
                    ? new Dictionary<string, string?> { ["dryRun"] = "true" }
                    : null;
                await _jsonSink.WriteLineAsync(jsonOutput.WriteEnvelope("build", exitCode, envelopeResult)).ConfigureAwait(false);
            }
            return exitCode;
        }
        finally
        {
            _console = originalConsole;
        }
    }

    private async Task<int> ExecuteInternalAsync(BuildSettings settings, CancellationToken cancellationToken)
    {
        if (settings.Reproducible)
        {
            var epoch = ResolveSourceDateEpoch(_console, _gitWorkingDirectory);
            if (epoch is null)
                return ExitCodes.RuntimeError;

            Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", epoch.Value.ToString());
        }

        if (settings.GenerateSbom)
            Environment.SetEnvironmentVariable("FALKFORGE_GENERATE_SBOM", "1");

        if (settings.NoSign)
            Environment.SetEnvironmentVariable("FALKFORGE_NO_SIGN", "1");

        var projectPath = Path.GetFullPath(settings.ProjectPath);

        if (!File.Exists(projectPath))
        {
            _console.WriteError($"File not found: {projectPath}");
            return ExitCodes.RuntimeError;
        }

        if (settings.Verbose)
            _console.MarkupLine($"[grey]Loading project: {Markup.Escape(projectPath)}[/]");

        var outputPath = settings.OutputPath ?? Directory.GetCurrentDirectory();
        var isJson = projectPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

        // MSIX is a separate output format with its own compiler path. JSON input
        // cannot produce MSIX today; script input would need Installer.BuildMsix()
        // inside the script, which BuildCommand does not invoke on its behalf.
        if (string.Equals(settings.Format, "msix", StringComparison.OrdinalIgnoreCase))
        {
            if (isJson)
            {
                _console.WriteError("MSIX packages cannot be built from JSON configuration. Use the C# script API with InstallerMsix.BuildMsix() / InstallerMsix.BuildMsixBundle() instead (see FalkForge.Compiler.Msix.InstallerMsix).");
                return ExitCodes.ValidationFailure;
            }

            if (!OperatingSystem.IsWindows())
            {
                _console.WriteError("MSIX compilation requires Windows.");
                return ExitCodes.RuntimeError;
            }

            _console.MarkupLine("[yellow]MSIX CLI build is experimental and not yet implemented.[/]");
            _console.MarkupLine("[grey]For MSIX/MSIX-bundle output, call InstallerMsix.BuildMsix() / InstallerMsix.BuildMsixBundle() directly from a .csx build script (see FalkForge.Compiler.Msix.InstallerMsix). Falling back to MSI build.[/]");
        }

        var loadResult = BuildInputResolver.Load(projectPath);
        if (loadResult.IsFailure)
        {
            _console.WriteError(loadResult.Error.Message);
            return ExitCodes.FromErrorKind(loadResult.Error.Kind);
        }

        var package = loadResult.Value;
        if (isJson)
            _console.MarkupLine($"[green]Loaded JSON config:[/] {Markup.Escape(package.Name)} v{package.Version}");

        // The optional signing section (JSON configs only) selects a bundle-integrity
        // signature backend. Structural validation happens here so a broken section fails
        // before any artifact is produced.
        SigningConfig? signingConfig = null;
        if (isJson)
        {
            var signingLoad = JsonConfigLoader.LoadSigningFromFile(projectPath);
            if (signingLoad.IsFailure)
            {
                _console.WriteError(signingLoad.Error.Message);
                return ExitCodes.FromErrorKind(signingLoad.Error.Kind);
            }
            signingConfig = signingLoad.Value;
        }

        if (settings.DryRun)
            return RunDryRun(package, outputPath);

        if (!OperatingSystem.IsWindows())
        {
            _console.WriteError("MSI compilation requires Windows.");
            return ExitCodes.RuntimeError;
        }

        // Resolve the signing provider BEFORE compiling: an unresolvable signing config
        // (e.g. an unset env var) must fail closed without leaving artifacts behind.
        var resolveResult = SigningProviderFactory.Create(signingConfig, Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory);
        if (resolveResult.IsFailure)
        {
            _console.WriteError(resolveResult.Error.Message);
            return ExitCodes.FromErrorKind(resolveResult.Error.Kind);
        }

        var resolvedSigning = resolveResult.Value;
        try
        {
            foreach (var warning in resolvedSigning.Warnings)
                _console.MarkupLine($"[yellow]Warning:[/] {Markup.Escape(warning)}");

            var compileResult = CompilePackage(package, outputPath, settings);
            if (compileResult.IsFailure)
            {
                _console.WriteError(compileResult.Error.Message);
                return ExitCodes.FromErrorKind(compileResult.Error.Kind);
            }

            _console.MarkupLine($"[green]Build succeeded:[/] {Markup.Escape(compileResult.Value)}");

            if (resolvedSigning.IsEnabled)
            {
                var bundleResult = await CompileSignedBundleAsync(
                    compileResult.Value, package, outputPath, resolvedSigning.Provider, cancellationToken).ConfigureAwait(false);
                if (bundleResult.IsFailure)
                {
                    _console.WriteError(bundleResult.Error.Message);
                    return ExitCodes.FromErrorKind(bundleResult.Error.Kind);
                }

                _console.MarkupLine($"[green]Signed bundle created:[/] {Markup.Escape(bundleResult.Value)}");
                _console.MarkupLine("[yellow]Note: this signed bundle uses a design-time placeholder engine stub and is NOT a runnable installer — its manifest signature verifies, but do not distribute it as an installer.[/]");
            }

            if (settings.GenerateWinGet)
            {
                var wingetResult = GenerateWinGetManifest(compileResult.Value, package, settings);
                if (wingetResult.IsFailure)
                    _console.MarkupLine($"[yellow]Warning:[/] WinGet manifest generation failed: {Markup.Escape(wingetResult.Error.Message)}");
                else
                    _console.MarkupLine("[green]WinGet manifest written alongside installer[/]");
            }

            return ExitCodes.Success;
        }
        finally
        {
            (resolvedSigning.Provider as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// Wraps the freshly compiled MSI in a single-package EXE bundle whose integrity manifest is
    /// signed by <paramref name="provider"/> (the C17 seam), using the ASYNC bundle build path so
    /// a genuinely asynchronous provider (e.g. SignServer) signs without blocking a thread.
    /// </summary>
    private static async Task<Result<string>> CompileSignedBundleAsync(
        string msiPath,
        PackageModel package,
        string outputPath,
        ISignatureProvider provider,
        CancellationToken cancellationToken)
    {
        var bundle = new BundleBuilder()
            .Name(package.Name)
            .Manufacturer(package.Manufacturer)
            .Version(package.Version.ToString())
            .Chain(chain => chain.MsiPackage(msiPath, p => p
                .Id("MainMsi")
                .DisplayName(package.Name)
                .Vital(true)))
            .Integrity(i => i.SigningProvider(provider))
            .Build();

        return await new BundleCompiler().CompileAsync(bundle, outputPath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes the dry-run path: runs model validation and a lightweight planning pass
    /// (component/file/feature counts, payload size, predicted output filename) without
    /// invoking the MSI compiler. No artifacts are written to <paramref name="outputPath"/>.
    /// Returns <see cref="ExitCodes.ValidationFailure"/> when the model has validation
    /// errors, <see cref="ExitCodes.Success"/> otherwise.
    /// </summary>
    private int RunDryRun(PackageModel package, string outputPath)
    {
        var validation = ModelValidator.Inspect(package);

        foreach (var warning in validation.Warnings)
            _console.MarkupLine($"[yellow]Warning {warning.RuleId}:[/] {Markup.Escape(warning.Message)}");

        foreach (var error in validation.Errors)
            _console.MarkupLine($"[red]Error {error.RuleId}:[/] {Markup.Escape(error.Message)}");

        if (!validation.IsValid)
        {
            _console.MarkupLine($"[red]Validation failed with {validation.Errors.Count()} error(s).[/]");
            return ExitCodes.ValidationFailure;
        }

        var fileCount = package.Files.Count;
        var componentCount = CountComponents(package);
        var featureCount = CountFeatures(package.Features);
        var payloadBytes = ComputePayloadBytes(package.Files);
        var outputFileName = $"{FileNameSanitizer.Sanitize(package.Name)}-{package.Version.ToString(3)}.msi";

        _console.MarkupLine("[cyan]Dry run:[/] no artifacts will be written.");
        _console.MarkupLine($"[grey]Package:[/] {Markup.Escape(package.Name)} v{package.Version}");
        _console.MarkupLine($"[grey]Output (would write):[/] {Markup.Escape(Path.Combine(outputPath, outputFileName))}");
        _console.MarkupLine($"[grey]Files:[/] {fileCount}");
        _console.MarkupLine($"[grey]Components:[/] {componentCount}");
        _console.MarkupLine($"[grey]Features:[/] {featureCount}");
        _console.MarkupLine($"[grey]Payload size:[/] {payloadBytes:N0} bytes");
        _console.MarkupLine("[green]Validation passed.[/]");

        return ExitCodes.Success;
    }

    private static int CountFeatures(IReadOnlyList<FeatureModel> features)
    {
        var total = 0;
        foreach (var feature in features)
        {
            total++;
            total += CountFeatures(feature.Children);
        }
        return total;
    }

    private static int CountComponents(PackageModel package)
    {
        // ComponentRefs across the feature tree approximates the component count
        // before MsiAuthoring resolves them. For dry-run reporting this is enough.
        var ids = new HashSet<string>(StringComparer.Ordinal);
        Visit(package.Features);
        return ids.Count;

        void Visit(IReadOnlyList<FeatureModel> features)
        {
            foreach (var feature in features)
            {
                foreach (var componentRef in feature.ComponentRefs)
                    ids.Add(componentRef);
                Visit(feature.Children);
            }
        }
    }

    private static long ComputePayloadBytes(IReadOnlyList<FileEntryModel> files)
    {
        long total = 0;
        foreach (var file in files)
        {
            try
            {
                if (File.Exists(file.SourcePath))
                    total += new FileInfo(file.SourcePath).Length;
            }
            catch
            {
                // Source missing or unreadable — surfaced by the real build; dry-run
                // reports best-effort payload size only.
            }
        }
        return total;
    }

    [SupportedOSPlatform("windows")]
    private Result<string> CompilePackage(PackageModel package, string outputPath, BuildSettings settings)
    {
        if (!Directory.Exists(outputPath))
            Directory.CreateDirectory(outputPath);

        var logger = new ConsoleOutputLogger(_console, settings.Verbose);
        var compiler = new MsiCompiler(new WindowsFileSystem(), [], logger);
        return compiler.Compile(package, outputPath);
    }

    private static Result<Unit> GenerateWinGetManifest(
        string installerPath,
        PackageModel package,
        BuildSettings settings)
    {
        try
        {
            using var fs = File.OpenRead(installerPath);
            var sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fs));

            var installerUrl = settings.WinGetInstallerUrl
                ?? $"https://example.com/{Path.GetFileName(installerPath)}";

            var identifier = WinGetManifestGenerator.SanitizePackageIdentifier(
                $"{package.Manufacturer}.{package.Name}");

            var options = new WinGetManifestOptions
            {
                PackageIdentifier = identifier,
                PackageVersion = package.Version.ToString(),
                Publisher = package.Manufacturer,
                PackageName = package.Name,
                ShortDescription = $"{package.Name} installer",
                InstallerUrl = installerUrl,
                InstallerSha256 = sha256
            };

            var yamlPath = installerPath + ".winget.yaml";
            return WinGetManifestGenerator.GenerateToFile(options, yamlPath);
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure(ErrorKind.IoError, $"WinGet: {ex.Message}");
        }
    }

    /// <summary>
    /// Resolves the SOURCE_DATE_EPOCH value for a reproducible build.
    /// Priority: SOURCE_DATE_EPOCH env var → git log HEAD timestamp.
    /// Returns null and writes an error if neither source is available.
    /// </summary>
    internal static long? ResolveSourceDateEpoch(IConsoleOutput console, string? gitWorkingDirectory = null)
    {
        var envValue = Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH");
        if (envValue is not null)
        {
            if (long.TryParse(envValue, out var parsed))
                return parsed;

            console.WriteError("RPR001: SOURCE_DATE_EPOCH is not a valid Unix timestamp.");
            return null;
        }

        try
        {
#pragma warning disable S4036 // PATH lookup is the platform contract for git (install location varies; developer CLI tool)
            var psi = new ProcessStartInfo("git", "log -1 --format=%ct")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = gitWorkingDirectory ?? Directory.GetCurrentDirectory()
            };
#pragma warning restore S4036
            using var proc = Process.Start(psi)!;
            var output = proc.StandardOutput.ReadToEnd().Trim();

            if (!proc.WaitForExit(10_000))
            {
                proc.Kill();
                // fall through to RPR002
            }
            else if (proc.ExitCode == 0 && long.TryParse(output, out var gitEpoch))
            {
                return gitEpoch;
            }
        }
        catch
        {
            // git not available or failed — fall through to RPR002
        }

        console.WriteError("RPR002: --reproducible requires SOURCE_DATE_EPOCH env var or a git repository.");
        return null;
    }
}
