namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Marker for a cabinet that should be embedded inside the produced MSI as
/// a binary stream. <see cref="StreamName"/> is the MSI stream name (typically
/// prefixed with <c>#</c>); <see cref="Source"/> supplies the compressed bytes.
/// </summary>
public sealed record CabinetEmbedding(string StreamName, StreamSource Source);
