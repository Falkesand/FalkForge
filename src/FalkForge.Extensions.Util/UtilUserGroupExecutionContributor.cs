using FalkForge.Extensibility;
using FalkForge.Extensions.Util.UserManagement;

namespace FalkForge.Extensions.Util;

/// <summary>
/// Bridges the Util extension's User/Group management to the reusable install-time execution seam. This is
/// deliberately a <b>separate</b> contributor from <see cref="UtilExecutionContributor"/> because it is the
/// only Util feature that carries a password secret and creates local accounts as SYSTEM — its steps flow a
/// credential through the seam's secure <c>CustomActionData</c> channel (see
/// <see cref="UtilUserGroupCommandFactory"/>). It makes the User/Group models <b>live</b>: deferred,
/// elevated custom actions that create local groups and users and group memberships on install and remove
/// them on uninstall, instead of the models being built and dropped.
/// </summary>
internal sealed class UtilUserGroupExecutionContributor(
    Func<IReadOnlyList<GroupModel>> groups,
    Func<IReadOnlyList<UserModel>> users) : IExecutionContributor
{
    public IReadOnlyList<ExecutionStep> GetExecutionSteps(ExtensionContext context)
        => UtilUserGroupCommandFactory.BuildSteps(groups(), users());
}
