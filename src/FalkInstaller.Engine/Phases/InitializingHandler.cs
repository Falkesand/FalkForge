namespace FalkInstaller.Engine.Phases;

using FalkInstaller.Engine.Protocol;
using FalkInstaller.Engine.Variables;

public sealed class InitializingHandler : IEnginePhaseHandler
{
    public EnginePhase Phase => EnginePhase.Initializing;

    public Task<EnginePhase> ExecuteAsync(EngineContext context, CancellationToken ct)
    {
        // Validate manifest is loaded
        if (context.Manifest is null)
        {
            context.ErrorMessage = "No manifest loaded";
            return Task.FromResult(EnginePhase.Failed);
        }

        // Populate built-in variables
        BuiltInVariables.Populate(context.Variables, context.Platform);

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
