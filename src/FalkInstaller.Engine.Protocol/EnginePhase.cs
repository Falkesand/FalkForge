namespace FalkInstaller.Engine.Protocol;

public enum EnginePhase
{
    Initializing,
    Detecting,
    Planning,
    Elevating,
    Applying,
    Completing,
    Failed,
    RollingBack,
    Shutdown
}
