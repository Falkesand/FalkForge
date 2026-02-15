namespace FalkInstaller.Compiler.Msi;

public readonly record struct CabinetWorkItem(
    string CabinetName,
    IReadOnlyList<ResolvedFile> FileEntries,
    CompressionLevel CompressionLevel);
