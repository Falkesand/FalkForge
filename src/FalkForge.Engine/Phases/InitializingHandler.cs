namespace FalkForge.Engine.Phases;

using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Variables;

public sealed class InitializingHandler : IEnginePhaseHandler
{
    private readonly TimeProvider _timeProvider;

    public InitializingHandler(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public EnginePhase Phase => EnginePhase.Initializing;

    public Task<EnginePhase> ExecuteAsync(EngineContext context, CancellationToken ct)
    {
        // Validate manifest is loaded
        if (context.Manifest is null)
        {
            context.ErrorMessage = "No manifest loaded";
            return Task.FromResult(EnginePhase.Failed);
        }

        // Wire dry-run mode from manifest
        if (context.Manifest.IsDryRun)
        {
            context.IsDryRun = true;
            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            context.DryRunLogPath = Path.Combine(
                Path.GetTempPath(),
                string.Concat(
                    "FalkForge-DryRun-",
                    nowUtc.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture),
                    ".log"));
        }

        // Populate built-in variables
        BuiltInVariables.Populate(context.Variables, context.Platform);

        // Seed author-defined variables from manifest
        foreach (var v in context.Manifest.Variables)
        {
            if (v.Secret)
            {
                if (v.DefaultValue is not null)
                    context.Variables.SetSecret(v.Name, v.DefaultValue);
            }
            else if (v.DefaultValue is not null)
            {
                context.Variables.Set(v.Name, v.DefaultValue);
            }
        }

        // Load persisted variable overrides from registry
        VariablePersistence.LoadPersistedVariables(
            context.Variables,
            context.Manifest.BundleId,
            context.Manifest.Scope,
            context.Manifest.Variables,
            context.Platform.Registry);

        // Set default install directory if not set
        if (string.IsNullOrEmpty(context.InstallDirectory))
        {
            context.InstallDirectory = context.Manifest.Scope == InstallScope.PerMachine
                ? Path.Combine(
                    context.Platform.Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    context.Manifest.Name)
                : Path.Combine(
                    context.Platform.Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    context.Manifest.Name);
        }

        return Task.FromResult(EnginePhase.Detecting);
    }
}
