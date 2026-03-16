using FalkForge.Models;

namespace FalkForge.Builders;

public sealed class FeatureBuilder
{
    private readonly List<FeatureModel> _children = [];
    private readonly List<FeatureConditionModel> _conditions = [];
    private readonly List<FileEntryModel> _files = [];
    private readonly string _id;

    internal FeatureBuilder(string id)
    {
        _id = id;
    }

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsRequired { get; set; }
    public bool IsDefault { get; set; } = true;
    public int DisplayLevel { get; set; } = 1;

    public FeatureBuilder Feature(string id, Action<FeatureBuilder> configure)
    {
        var builder = new FeatureBuilder(id);
        configure(builder);
        _children.Add(builder.Build());
        return this;
    }

    public FeatureBuilder Condition(string condition, int level)
    {
        _conditions.Add(new FeatureConditionModel { Condition = condition, Level = level });
        return this;
    }

    public FeatureBuilder Condition(string condition)
    {
        return Condition(condition, 0);
    }

    public FeatureBuilder Condition(Condition condition, int level)
    {
        return Condition(condition.ToString(), level);
    }

    public FeatureBuilder Condition(Condition condition)
    {
        return Condition(condition.ToString(), 0);
    }

    public FeatureBuilder Files(Action<FileSetBuilder> configure)
    {
        var builder = new FileSetBuilder();
        configure(builder);
        _files.AddRange(builder.Build());
        return this;
    }

    internal FeatureModel Build()
    {
        return new FeatureModel
        {
            Id = _id,
            Title = string.IsNullOrEmpty(Title) ? _id : Title,
            Description = Description,
            IsRequired = IsRequired,
            IsDefault = IsDefault,
            DisplayLevel = DisplayLevel,
            Children = _children,
            Conditions = _conditions
        };
    }
}