namespace FalkForge.Extensions.Iis;

public sealed record AppPoolRef
{
    public AppPoolRef(string Id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        this.Id = Id;
    }

    public string Id { get; }
}