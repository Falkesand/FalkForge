namespace FalkForge.Models;

public sealed record ComClassModel
{
    public required Guid ClassId { get; init; }
    public required ComServerType ServerType { get; init; }
    public string? ProgId { get; init; }
    public string? Description { get; init; }
    public ComThreadingModel ThreadingModel { get; init; } = ComThreadingModel.Apartment;
    public Guid? AppId { get; init; }
    public string? ComponentRef { get; init; }
}
