using System.Diagnostics.CodeAnalysis;
using FalkForge.Cli.Settings;
using FalkForge.Compiler.Msi;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FalkForge.Cli.Commands;

/// <summary>
/// Implements <c>forge loc export</c>: exports the built-in localization JSON (baked into
/// FalkForge.Compiler.Msi as embedded resources) so users never need to clone the FalkForge repo
/// to start an override file. The written content is the exact embedded resource, byte-faithful.
/// Cross-platform: reads embedded resources only, no msi.dll P/Invoke required.
/// <para>
/// Overwrite semantics: an existing file at the target path is silently replaced (no confirmation
/// prompt, no <c>--force</c> flag). Export is an idempotent, non-destructive read of built-in
/// resources into a file the caller named themselves -- re-running it is expected and safe to
/// repeat, unlike <c>forge init</c> which scaffolds a whole project and guards against clobbering
/// hand-written files.
/// </para>
/// </summary>
public sealed class LocExportCommand : Command<LocExportSettings>
{
    private readonly IConsoleOutput _console;

    public LocExportCommand() : this(new SpectreConsoleOutput()) { }

    public LocExportCommand(IConsoleOutput console)
    {
        _console = console;
    }

    protected override int Execute([NotNull] CommandContext context, [NotNull] LocExportSettings settings, CancellationToken cancellationToken)
    {
        if (settings.List)
        {
            foreach (var culture in BuiltInLocalizationExtensions.BuiltInCultureNames)
                _console.WriteLine(culture);
            return ExitCodes.Success;
        }

        string[] cultures;
        if (string.IsNullOrWhiteSpace(settings.Culture))
        {
            // Exporting every built-in culture into one named file is ambiguous -- which
            // culture's content would go there? Fail loud instead of guessing or silently
            // creating a directory literally named "foo.json".
            if (settings.Output.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                _console.WriteError(
                    $"'{settings.Output}' looks like a single file, but no --culture was given and " +
                    $"{BuiltInLocalizationExtensions.BuiltInCultureNames.Count} built-in cultures would be " +
                    "exported. Pass --culture <name> to export one culture to that exact file, or point " +
                    "--output at a directory to export all cultures.");
                return ExitCodes.ValidationFailure;
            }

            cultures = [.. BuiltInLocalizationExtensions.BuiltInCultureNames];
        }
        else if (BuiltInLocalizationExtensions.TryGetCanonicalCultureName(settings.Culture, out var canonical))
        {
            cultures = [canonical];
        }
        else
        {
            var available = string.Join(", ", BuiltInLocalizationExtensions.BuiltInCultureNames);
            _console.WriteError($"Unknown culture '{settings.Culture}'. Available built-in cultures: {available}");
            return ExitCodes.ValidationFailure;
        }

        // A single explicit --culture with a .json-suffixed --output names the override file
        // directly. Anything else -- default output, a directory path, or exporting every
        // built-in culture -- writes "<culture>.json" per culture into the output directory
        // (created if missing); multiple cultures can never share one file path.
        var writeAsExplicitFile = cultures.Length == 1
                                   && !string.IsNullOrWhiteSpace(settings.Culture)
                                   && settings.Output.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

        try
        {
            foreach (var culture in cultures)
            {
                var bytes = BuiltInLocalizationExtensions.GetBuiltInCultureJsonBytes(culture);

                string targetPath;
                if (writeAsExplicitFile)
                {
                    targetPath = settings.Output;
                    var directory = Path.GetDirectoryName(Path.GetFullPath(targetPath));
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);
                }
                else
                {
                    Directory.CreateDirectory(settings.Output);
                    targetPath = Path.Combine(settings.Output, $"{culture}.json");
                }

                File.WriteAllBytes(targetPath, bytes);
                _console.MarkupLine($"[green]Exported[/] {Markup.Escape(culture)} -> {Markup.Escape(targetPath)}");
            }
        }
        catch (IOException ex)
        {
            _console.WriteError(ex.Message);
            return ExitCodes.RuntimeError;
        }
        catch (UnauthorizedAccessException ex)
        {
            _console.WriteError(ex.Message);
            return ExitCodes.RuntimeError;
        }

        return ExitCodes.Success;
    }
}
