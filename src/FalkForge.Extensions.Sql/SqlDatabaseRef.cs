namespace FalkForge.Extensions.Sql;

public sealed record SqlDatabaseRef
{
    public string Id { get; }

    public SqlDatabaseRef(string Id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        this.Id = Id;
    }
}
