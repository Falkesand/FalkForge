namespace FalkInstaller.Engine.Protocol.Manifest;

public sealed record RollbackBoundaryManifestChainItem(RollbackBoundaryInfo Boundary) : ManifestChainItem;
