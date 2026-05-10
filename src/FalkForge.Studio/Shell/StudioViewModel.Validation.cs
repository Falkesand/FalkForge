using FalkForge.Studio.Project;

namespace FalkForge.Studio.Shell;

public sealed partial class StudioViewModel
{
    /// <summary>
    /// Runs validation immediately and applies results to <see cref="ValidationMessages"/>.
    /// </summary>
    public void RunValidation(string baseDirectory = ".")
    {
        _baseDirectory = baseDirectory;
        var messages = RunValidationCore(baseDirectory);
        ApplyValidationResults(messages);
    }

    /// <summary>
    /// Runs the validation checks and returns the result list without applying it.
    /// Visible internally for tests that need to inspect raw validation output.
    /// </summary>
    internal List<ValidationMessage> RunValidationCore(string baseDirectory)
    {
        var messages = new List<ValidationMessage>();

        if (string.IsNullOrWhiteSpace(_project.Product.Name))
            messages.Add(new ValidationMessage { Code = "STU001", Severity = "Error", Message = "Product name is empty.", EditorKey = "product" });

        if (string.IsNullOrWhiteSpace(_project.Product.Manufacturer))
            messages.Add(new ValidationMessage { Code = "STU002", Severity = "Error", Message = "Manufacturer is empty.", EditorKey = "product" });

        if (!Version.TryParse(_project.Product.Version, out _))
            messages.Add(new ValidationMessage { Code = "STU003", Severity = "Error", Message = $"Version '{_project.Product.Version}' is not valid.", EditorKey = "product" });

        if (_project.Features.Count == 0)
            messages.Add(new ValidationMessage { Code = "STU004", Severity = "Error", Message = "No features defined.", EditorKey = "features" });

        foreach (var feature in _project.Features)
            CheckFeatureFiles(messages, feature);

        if (!string.IsNullOrWhiteSpace(_project.Product.UpgradeCode) && !Guid.TryParse(_project.Product.UpgradeCode, out _))
            messages.Add(new ValidationMessage { Code = "STU006", Severity = "Error", Message = $"Upgrade code '{_project.Product.UpgradeCode}' is not a valid GUID.", EditorKey = "product" });

        var modelResult = StudioBuildService.BuildModel(_project, baseDirectory);
        if (modelResult.IsFailure)
        {
            var alreadyCovered = messages.Any(m => modelResult.Error.Message.Contains(m.Message, StringComparison.OrdinalIgnoreCase));
            if (!alreadyCovered)
                messages.Add(new ValidationMessage { Code = "STU099", Severity = "Error", Message = modelResult.Error.Message });
        }

        return messages;
    }

    private static void CheckFeatureFiles(List<ValidationMessage> messages, Project.FeatureSection feature)
    {
        if (feature.Files.Count == 0 && (feature.Features is null || feature.Features.Count == 0))
            messages.Add(new ValidationMessage { Code = "STU005", Severity = "Warning", Message = $"Feature '{feature.Id}' has no files.", EditorKey = "features" });

        if (feature.Features is not null)
        {
            foreach (var sub in feature.Features)
                CheckFeatureFiles(messages, sub);
        }
    }

    private void ApplyValidationResults(List<ValidationMessage> messages)
    {
        ValidationMessages.Clear();
        foreach (var msg in messages)
            ValidationMessages.Add(msg);
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(WarningCount));
    }

    /// <summary>
    /// Saves the current project state to the undo stack and schedules a debounced
    /// validation pass. Call this after any editor mutates the project.
    /// </summary>
    public void SaveUndoState()
    {
        _undoManager.SaveState(_project);
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        ScheduleValidation();
    }

    /// <summary>
    /// Schedules a validation pass after a 300 ms debounce. Cancels any pending pass.
    /// Fire-and-forget; results are applied on the UI thread via the dispatcher.
    /// </summary>
    public void ScheduleValidation()
    {
        _validationDebounce?.Cancel();
        _validationDebounce?.Dispose();
        var cts = new CancellationTokenSource();
        _validationDebounce = cts;
        var ct = cts.Token;
        var baseDir = _baseDirectory;

#pragma warning disable CS4014 // Fire-and-forget is intentional for debounced background validation
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var messages = RunValidationCore(baseDir);

            if (!ct.IsCancellationRequested)
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => ApplyValidationResults(messages));
        }, ct);
#pragma warning restore CS4014
    }
}
