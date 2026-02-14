namespace FalkInstaller;

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
    NotSupported
}
