namespace FalkForge;

public enum ErrorKind
{
    None,
    Validation,
    FileNotFound,
    DirectoryNotFound,
    InvalidConfiguration,
    IoError,
    CompilationError,
    SecurityError,
    PlatformError,
    InvalidOperation,
    NotSupported,
    ProtocolError,
    TransportError,
    HandshakeError,
    EngineError,
    ElevationError,
    BundleError,
    PayloadError,
    CacheError,
    RollbackError,
    DetectionError,
    PlanningError,
    ExecutionError,
    DownloadError,
    LayoutError,
    PluginError
}