using FalkForge.Extensibility;
using FalkForge.Extensions.Util.FileShare;
using FalkForge.Extensions.Util.InternetShortcut;
using FalkForge.Extensions.Util.QuietExec;
using FalkForge.Extensions.Util.RemoveFolderEx;

namespace FalkForge.Extensions.Util;

/// <summary>
/// Bridges the four non-secret Util execution features — QuietExec, RemoveFolderEx, FileShare,
/// InternetShortcut — to the reusable install-time execution seam, mirroring
/// <c>FirewallExecutionContributor</c>. Where each feature's table contributor (where one exists)
/// records inspectable data, this contributor makes the feature <b>live</b>. User/Group management is
/// deliberately excluded here — it carries password secrets and belongs to a separate execution
/// contributor with its own CustomActionData-secured channel.
/// </summary>
internal sealed class UtilExecutionContributor(
    Func<IReadOnlyList<QuietExecModel>> quietExecs,
    Func<IReadOnlyList<RemoveFolderExModel>> removeFolderExes,
    Func<IReadOnlyList<FileShareModel>> fileShares,
    Func<IReadOnlyList<InternetShortcutModel>> internetShortcuts) : IExecutionContributor
{
    public IReadOnlyList<ExecutionStep> GetExecutionSteps(ExtensionContext context)
    {
        var steps = new List<ExecutionStep>();
        steps.AddRange(QuietExecCommandFactory.BuildSteps(quietExecs()));
        steps.AddRange(RemoveFolderExCommandFactory.BuildSteps(removeFolderExes()));
        steps.AddRange(FileShareCommandFactory.BuildSteps(fileShares()));
        steps.AddRange(InternetShortcutCommandFactory.BuildSteps(internetShortcuts()));
        return steps;
    }
}
