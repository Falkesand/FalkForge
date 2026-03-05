namespace FalkForge.Engine;

internal interface IUpdateLauncher
{
    Result<Unit> Launch(string updatePath);
}
