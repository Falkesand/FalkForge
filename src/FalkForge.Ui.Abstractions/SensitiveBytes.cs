// SensitiveBytes lives in FalkForge.Core (namespace FalkForge) so that
// FalkForge.Engine.Protocol can reference it without creating a circular dependency.
// Re-export into this namespace for backward compatibility with callers.
global using SensitiveBytes = FalkForge.SensitiveBytes;
