namespace FalkInstaller.Engine.Tests.Execution;

using FalkInstaller.Engine.Execution;
using Xunit;

public sealed class ExitCodeMappingTests
{
    [Fact]
    public void Map_KnownExitCode_ReturnsMappedBehavior()
    {
        var mappings = new Dictionary<int, ExitCodeBehavior>
        {
            [42] = ExitCodeBehavior.RebootRequired
        };
        var mapping = new ExitCodeMapping(mappings, ExitCodeBehavior.Failure);

        var result = mapping.Map(42);

        Assert.Equal(ExitCodeBehavior.RebootRequired, result);
    }

    [Fact]
    public void Map_UnknownExitCode_ReturnsDefaultBehavior()
    {
        var mappings = new Dictionary<int, ExitCodeBehavior>
        {
            [0] = ExitCodeBehavior.Success
        };
        var mapping = new ExitCodeMapping(mappings, ExitCodeBehavior.Failure);

        var result = mapping.Map(9999);

        Assert.Equal(ExitCodeBehavior.Failure, result);
    }

    [Fact]
    public void Default_ZeroMapsToSuccess()
    {
        var mapping = ExitCodeMapping.Default();

        Assert.Equal(ExitCodeBehavior.Success, mapping.Map(0));
    }

    [Fact]
    public void Default_3010MapsToRebootRequired()
    {
        var mapping = ExitCodeMapping.Default();

        Assert.Equal(ExitCodeBehavior.RebootRequired, mapping.Map(3010));
    }

    [Fact]
    public void Default_1602MapsToFailure()
    {
        var mapping = ExitCodeMapping.Default();

        Assert.Equal(ExitCodeBehavior.Failure, mapping.Map(1602));
    }

    [Fact]
    public void Default_1618MapsToFailure()
    {
        var mapping = ExitCodeMapping.Default();

        Assert.Equal(ExitCodeBehavior.Failure, mapping.Map(1618));
    }

    [Fact]
    public void Default_UnknownCodeMapsToFailure()
    {
        var mapping = ExitCodeMapping.Default();

        Assert.Equal(ExitCodeBehavior.Failure, mapping.Map(7777));
    }

    [Fact]
    public void CustomMapping_OverridesDefaultBehavior()
    {
        var mappings = new Dictionary<int, ExitCodeBehavior>
        {
            [0] = ExitCodeBehavior.Success,
            [100] = ExitCodeBehavior.ScheduleReboot
        };
        var mapping = new ExitCodeMapping(mappings, ExitCodeBehavior.Success);

        Assert.Equal(ExitCodeBehavior.ScheduleReboot, mapping.Map(100));
        Assert.Equal(ExitCodeBehavior.Success, mapping.Map(999));
    }

    [Fact]
    public void FromDictionary_NullDictionary_ReturnsDefault()
    {
        var mapping = ExitCodeMapping.FromDictionary(null);

        Assert.Equal(ExitCodeBehavior.Success, mapping.Map(0));
        Assert.Equal(ExitCodeBehavior.RebootRequired, mapping.Map(3010));
        Assert.Equal(ExitCodeBehavior.Failure, mapping.Map(1602));
    }

    [Fact]
    public void FromDictionary_EmptyDictionary_ReturnsDefault()
    {
        var mapping = ExitCodeMapping.FromDictionary(new Dictionary<int, ExitCodeBehavior>());

        Assert.Equal(ExitCodeBehavior.Success, mapping.Map(0));
        Assert.Equal(ExitCodeBehavior.RebootRequired, mapping.Map(3010));
    }

    [Fact]
    public void FromDictionary_CustomCodes_MergedWithDefaults()
    {
        var custom = new Dictionary<int, ExitCodeBehavior>
        {
            [3010] = ExitCodeBehavior.ScheduleReboot, // Override default 3010 behavior
            [5000] = ExitCodeBehavior.Success
        };
        var mapping = ExitCodeMapping.FromDictionary(custom);

        // Custom override
        Assert.Equal(ExitCodeBehavior.ScheduleReboot, mapping.Map(3010));
        // Custom addition
        Assert.Equal(ExitCodeBehavior.Success, mapping.Map(5000));
        // Default preserved
        Assert.Equal(ExitCodeBehavior.Success, mapping.Map(0));
    }

    [Fact]
    public void Constructor_DoesNotMutateOriginalDictionary()
    {
        var original = new Dictionary<int, ExitCodeBehavior>
        {
            [0] = ExitCodeBehavior.Success
        };
        var mapping = new ExitCodeMapping(original, ExitCodeBehavior.Failure);

        // Mutate original after construction
        original[0] = ExitCodeBehavior.RebootRequired;

        // Mapping should still return original value
        Assert.Equal(ExitCodeBehavior.Success, mapping.Map(0));
    }
}
