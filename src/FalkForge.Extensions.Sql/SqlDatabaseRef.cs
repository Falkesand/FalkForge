namespace FalkForge.Extensions.Sql;

public sealed record SqlDatabaseRef
{
    public SqlDatabaseRef(string Id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        this.Id = Id;
    }

    public string Id { get; }
}