using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using System.Text.Json;
using FalkForge.Cli.Diff;
using FalkForge.Cli.Settings;
using FalkForge.Decompiler;
using FalkForge.Engine.Protocol.Bundle;
using Spectre.Console.Cli;

namespace FalkForge.Cli.Commands;

/// <summary>
/// Diffs two installer artifacts (MSI vs MSI, or bundle EXE vs bundle EXE) and emits a
/// human-readable or machine-parseable summary of what changed — packages, features, services,
/// registry entries, files, shortcuts, and upgrade entries.
/// <para>
/// Exit code 0 is returned whenever the diff itself completes successfully, regardless of
/// whether differences were found. A non-zero exit code indicates a runtime failure (missing
/// file, unreadable artifact, unsupported artifact type).
/// </para>
/// </summary>
[Description("Diff two installer artifacts (MSI or EXE bundle) and report what changed")]
internal sealed class PlanDiffCommand : Command<PlanDiffSettings>
{
    private readonly IConsoleOutput _output;
    private readonly System.IO.TextWriter _textSink;

    public PlanDiffCommand() : this(new SpectreConsoleOutput()) { }

    public PlanDiffCommand(IConsoleOutput output, System.IO.TextWriter? textSink = null)
    {
        _output   = output;
        _textSink = textSink ?? Console.Out;
    }

    public override int Execute(
        [NotNull] CommandContext context,
        [NotNull] PlanDiffSettings settings,
        CancellationToken cancellationToken)
    {
        var oldPath = Path.GetFullPath(settings.OldPath);
        var newPath = Path.GetFullPath(settings.NewPath);

        if (!File.Exists(oldPath))
        {
            _output.WriteError($"File not found: {oldPath}");
            return ExitCodes.RuntimeError;
        }

        if (!File.Exists(newPath))
        {
            _output.WriteError($"File not found: {newPath}");
            return ExitCodes.RuntimeError;
        }

        var oldExt = Path.GetExtension(oldPath).ToLowerInvariant();
        var newExt = Path.GetExtension(newPath).ToLowerInvariant();

        if (oldExt != newExt)
        {
            _output.WriteError(
                $"Artifact types differ: '{oldExt}' vs '{newExt}'. " +
                "Both paths must be the same type (both .msi or both .exe).");
            return ExitCodes.RuntimeError;
        }

        return oldExt switch
        {
            ".exe" => DiffBundles(oldPath, newPath, settings),
            ".msi" or ".msm" => DiffMsiArtifacts(oldPath, newPath, settings),
            _ => UnknownExtension(oldExt),
        };
    }

    // -------------------------------------------------------------------------
    // Bundle mode (cross-platform)
    // -------------------------------------------------------------------------
    private int DiffBundles(string oldPath, string newPath, PlanDiffSettings settings)
    {
        var oldExtract = BundleReader.Extract(oldPath);
        if (oldExtract.IsFailure)
        {
            _output.WriteError($"Failed to read bundle '{oldPath}': {oldExtract.Error.Message}");
            return ExitCodes.RuntimeError;
        }

        var newExtract = BundleReader.Extract(newPath);
        if (newExtract.IsFailure)
        {
            _output.WriteError($"Failed to read bundle '{newPath}': {newExtract.Error.Message}");
            return ExitCodes.RuntimeError;
        }

        var oldContent = oldExtract.Value;
        var newContent = newExtract.Value;

        if (oldContent.ManifestJsonBytes is null || oldContent.ManifestJsonBytes.Length == 0)
        {
            _output.WriteError($"Bundle '{oldPath}' does not contain an embedded manifest.");
            return ExitCodes.RuntimeError;
        }

        if (newContent.ManifestJsonBytes is null || newContent.ManifestJsonBytes.Length == 0)
        {
            _output.WriteError($"Bundle '{newPath}' does not contain an embedded manifest.");
            return ExitCodes.RuntimeError;
        }

        Engine.Protocol.Manifest.InstallerManifest oldManifest;
        Engine.Protocol.Manifest.InstallerManifest newManifest;

        try
        {
            oldManifest = JsonSerializer.Deserialize(oldContent.ManifestJsonBytes,
                PlanDiffManifestJsonContext.Default.InstallerManifest)
                ?? throw new InvalidOperationException("Manifest deserialized to null.");
        }
        catch (Exception ex)
        {
            _output.WriteError($"Failed to deserialize manifest from '{oldPath}': {ex.Message}");
            return ExitCodes.RuntimeError;
        }

        try
        {
            newManifest = JsonSerializer.Deserialize(newContent.ManifestJsonBytes,
                PlanDiffManifestJsonContext.Default.InstallerManifest)
                ?? throw new InvalidOperationException("Manifest deserialized to null.");
        }
        catch (Exception ex)
        {
            _output.WriteError($"Failed to deserialize manifest from '{newPath}': {ex.Message}");
            return ExitCodes.RuntimeError;
        }

        var result = BundlePlanDiff.Diff(oldPath, newPath, oldManifest, newManifest);
        return Render(result, settings);
    }

    // -------------------------------------------------------------------------
    // MSI mode (Windows-only at runtime; the decompiler uses msi.dll P/Invoke)
    // -------------------------------------------------------------------------
    [SupportedOSPlatform("windows")]
    private int DiffMsiArtifactsWindows(string oldPath, string newPath, PlanDiffSettings settings)
    {
        var oldDecompiler = new MsiDecompiler();
        var oldRecipe = oldDecompiler.DecompileToRecipe(oldPath);
        if (oldRecipe.IsFailure)
        {
            _output.WriteError($"Failed to read MSI '{oldPath}': {oldRecipe.Error.Message}");
            return ExitCodes.RuntimeError;
        }

        var newDecompiler = new MsiDecompiler();
        var newRecipeResult = newDecompiler.DecompileToRecipe(newPath);
        if (newRecipeResult.IsFailure)
        {
            _output.WriteError($"Failed to read MSI '{newPath}': {newRecipeResult.Error.Message}");
            return ExitCodes.RuntimeError;
        }

        var result = MsiPlanDiff.Diff(oldPath, newPath, oldRecipe.Value, newRecipeResult.Value);
        return Render(result, settings);
    }

    private int DiffMsiArtifacts(string oldPath, string newPath, PlanDiffSettings settings)
    {
        if (!OperatingSystem.IsWindows())
        {
            _output.WriteError(
                "MSI diff requires Windows. " +
                "Run on a Windows host or use bundle EXE artifacts instead.");
            return ExitCodes.RuntimeError;
        }

        return DiffMsiArtifactsWindows(oldPath, newPath, settings);
    }

    // -------------------------------------------------------------------------
    // Rendering
    // -------------------------------------------------------------------------
    private int Render(PlanDiffResult result, PlanDiffSettings settings)
    {
        if (settings.Json)
        {
            _textSink.WriteLine(PlanDiffRenderer.RenderJson(result));
        }
        else if (settings.Markdown)
        {
            _textSink.WriteLine(PlanDiffRenderer.RenderMarkdown(result));
        }
        else
        {
            PlanDiffRenderer.RenderSpectre(result, _output);
        }

        return ExitCodes.Success;
    }

    private int UnknownExtension(string ext)
    {
        _output.WriteError(
            $"Unsupported artifact type '{ext}'. " +
            "Supported types: .msi, .msm (MSI mode, Windows-only), .exe (bundle mode, cross-platform).");
        return ExitCodes.RuntimeError;
    }
}
