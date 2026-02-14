namespace FalkInstaller.Builders;

using FalkInstaller.Models;

public sealed class FeatureBuilder
{
    private readonly string _id;
    private readonly List<FeatureModel> _children = [];
    private readonly List<FileEntryModel> _files = [];

    internal FeatureBuilder(string id) => _id = id;

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsRequired { get; set; }
    public bool IsDefault { get; set; } = true;

    public FeatureBuilder Feature(string id, Action<FeatureBuilder> configure)
    {
        var builder = new FeatureBuilder(id);
        configure(builder);
        _children.Add(builder.Build());
        return this;
    }

    public FeatureBuilder Files(Action<FileSetBuilder> configure)
    {
        var builder = new FileSetBuilder();
        configure(builder);
        _files.AddRange(builder.Build());
        return this;
    }

    internal FeatureModel Build() => new()
    {
        Id = _id,
        Title = string.IsNullOrEmpty(Title) ? _id : Title,
        Description = Description,
        IsRequired = IsRequired,
        IsDefault = IsDefault,
        Children = _children
    };
}
