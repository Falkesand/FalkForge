namespace FalkForge.Compiler.Msi;

public readonly record struct CabinetBuildResult(
    string CabinetName,
    string OutputPath,
    int FileCount,
    long CompressedSize);