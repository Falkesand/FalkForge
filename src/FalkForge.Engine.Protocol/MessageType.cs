namespace FalkForge.Engine.Protocol;

public enum MessageType : ushort
{
    // Engine -> UI (0x01xx)
    DetectBegin = 0x0101,
    DetectComplete = 0x0102,
    PlanBegin = 0x0103,
    PlanComplete = 0x0104,
    ApplyBegin = 0x0105,
    ApplyComplete = 0x0106,
    Progress = 0x0107,
    Error = 0x0108,
    PhaseChanged = 0x0109,
    Log = 0x010A,
    ShutdownResponse = 0x010B,
    UpdateAvailable = 0x010C,
    UpdateReady = 0x010D,

    // UI -> Engine (0x02xx)
    Cancel = 0x0201,
    ShutdownRequest = 0x0202,
    SetInstallDirectory = 0x0203,
    SetFeatureSelection = 0x0204,
    RequestDetect = 0x0205,
    RequestPlan = 0x0206,
    RequestApply = 0x0207,
    SetProperty = 0x0208,
    SetSecureProperty = 0x0209,

    // Engine -> Elevated (0x03xx)
    ElevateExecute = 0x0301,

    // Elevated -> Engine (0x04xx)
    ElevateResult = 0x0401,
}
