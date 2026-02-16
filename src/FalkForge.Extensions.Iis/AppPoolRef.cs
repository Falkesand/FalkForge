namespace FalkForge.Extensions.Iis;

public sealed record AppPoolRef
{
    public string Id { get; }

    public AppPoolRef(string Id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        this.Id = Id;
    }
}
