namespace FalkForge.Compiler.Bundle;

public sealed record BundleVariableModel(
    string Name,
    BundleVariableType Type,
    string? DefaultValue,
    bool Persisted,
    bool Hidden,
    bool Secret
);
