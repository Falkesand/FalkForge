namespace FalkInstaller.Engine.Execution;

using FalkInstaller.Engine.Planning;
using FalkInstaller.Engine.Protocol.Manifest;

public sealed class PackageExecutor
{
    private readonly MsiExecutor _msiExecutor;

    public PackageExecutor(MsiExecutor msiExecutor)
    {
        _msiExecutor = msiExecutor;
    }

    public async Task<Result<Unit>> ExecuteAsync(PlanAction action, CancellationToken ct)
    {
        return action.Package.Type switch
        {
            PackageType.MsiPackage => await _msiExecutor.ExecuteAsync(action, ct),
            PackageType.ExePackage => Result<Unit>.Failure(
                ErrorKind.ExecutionError, "EXE package execution not yet implemented"),
            PackageType.NetRuntime => Result<Unit>.Failure(
                ErrorKind.ExecutionError, ".NET runtime installation not yet implemented"),
            _ => Result<Unit>.Failure(
                ErrorKind.ExecutionError, $"Unknown package type: {action.Package.Type}")
        };
    }
}
