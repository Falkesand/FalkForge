using System.Diagnostics;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Shell;

public sealed partial class StudioViewModel
{
    /// <summary>
    /// Runs the project compiler asynchronously and updates the output panel.
    /// Triggers a validation pass after completion.
    /// </summary>
    public async Task BuildAsync(string baseDirectory)
    {
        _baseDirectory = baseDirectory;
        IsBuildInProgress = true;
        ShowBuildProgress = true;
        BuildProgress = 0;
        BuildSummary = null;
        var sw = Stopwatch.StartNew();
        OutputText = $"[{LocalNowHms()}] Build started\n";

        try
        {
            OutputText += $"[{LocalNowHms()}] Validating...\n";
            BuildProgress = 10;

            var result = await Task.Run(() => StudioBuildService.Compile(_project, baseDirectory));

            BuildProgress = 100;
            sw.Stop();

            if (result.IsSuccess)
            {
                OutputText += $"[{LocalNowHms()}] Output: {result.Value}\n";
                BuildSummary = $"Build succeeded in {sw.Elapsed.TotalSeconds:F1}s";
                BuildSucceeded = true;
            }
            else
            {
                OutputText += $"[{LocalNowHms()}] Error: {result.Error.Message}\n";
                BuildSummary = $"Build failed — {result.Error.Message}";
                BuildSucceeded = false;
            }
        }
        catch (Exception ex)
        {
            OutputText += $"[{LocalNowHms()}] Exception: {ex.Message}\n";
            BuildSummary = $"Build failed — {ex.Message}";
            BuildSucceeded = false;
        }
        finally
        {
            IsBuildInProgress = false;
            ShowBuildProgress = false;
        }

        RunValidation(baseDirectory);
    }
}
