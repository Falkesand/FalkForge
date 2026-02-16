namespace FalkForge.Engine.Execution;

public sealed class ExitCodeMapping
{
    private readonly IReadOnlyDictionary<int, ExitCodeBehavior> _mappings;
    private readonly ExitCodeBehavior _defaultBehavior;

    public ExitCodeMapping(IReadOnlyDictionary<int, ExitCodeBehavior> mappings, ExitCodeBehavior defaultBehavior)
    {
        _mappings = new Dictionary<int, ExitCodeBehavior>(mappings);
        _defaultBehavior = defaultBehavior;
    }

    public ExitCodeBehavior Map(int exitCode)
    {
        return _mappings.TryGetValue(exitCode, out var behavior)
            ? behavior
            : _defaultBehavior;
    }

    public static ExitCodeMapping Default()
    {
        var mappings = new Dictionary<int, ExitCodeBehavior>
        {
            [0] = ExitCodeBehavior.Success,
            [3010] = ExitCodeBehavior.RebootRequired,
            [1602] = ExitCodeBehavior.Failure,
            [1618] = ExitCodeBehavior.Failure
        };

        return new ExitCodeMapping(mappings, ExitCodeBehavior.Failure);
    }

    public static ExitCodeMapping FromDictionary(IReadOnlyDictionary<int, ExitCodeBehavior>? exitCodes)
    {
        if (exitCodes is null || exitCodes.Count == 0)
        {
            return Default();
        }

        // Merge custom codes on top of defaults
        var defaults = new Dictionary<int, ExitCodeBehavior>
        {
            [0] = ExitCodeBehavior.Success,
            [3010] = ExitCodeBehavior.RebootRequired,
            [1602] = ExitCodeBehavior.Failure,
            [1618] = ExitCodeBehavior.Failure
        };

        foreach (var kvp in exitCodes)
        {
            defaults[kvp.Key] = kvp.Value;
        }

        return new ExitCodeMapping(defaults, ExitCodeBehavior.Failure);
    }
}
