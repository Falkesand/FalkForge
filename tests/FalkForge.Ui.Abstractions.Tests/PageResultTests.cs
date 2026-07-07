namespace FalkForge.Ui.Abstractions.Tests;

using Xunit;

public sealed class PageResultTests
{
    public static TheoryData<PageResult, PageResult> SingletonData => new()
    {
        { PageResult.Next, PageResult.Next },
        { PageResult.Previous, PageResult.Previous },
        { PageResult.Finish, PageResult.Finish },
        { PageResult.Cancel, PageResult.Cancel },
        { PageResult.Install, PageResult.Install },
        { PageResult.Uninstall, PageResult.Uninstall },
        { PageResult.Repair, PageResult.Repair }
    };

    [Theory]
    [MemberData(nameof(SingletonData))]
    public void Singleton_returns_same_instance_on_repeated_access(PageResult first, PageResult second)
    {
        Assert.Same(first, second);
    }

    [Theory]
    [InlineData(nameof(PageResult.Next), PageResultKind.Next)]
    [InlineData(nameof(PageResult.Previous), PageResultKind.Previous)]
    [InlineData(nameof(PageResult.Finish), PageResultKind.Finish)]
    [InlineData(nameof(PageResult.Cancel), PageResultKind.Cancel)]
    [InlineData(nameof(PageResult.Install), PageResultKind.Install)]
    [InlineData(nameof(PageResult.Uninstall), PageResultKind.Uninstall)]
    [InlineData(nameof(PageResult.Repair), PageResultKind.Repair)]
    public void Singleton_has_correct_kind(string fieldName, PageResultKind expectedKind)
    {
        var result = GetSingletonByName(fieldName);

        Assert.Equal(expectedKind, result.Kind);
    }

    [Fact]
    public void Stay_without_message_has_kind_stay_and_null_message()
    {
        var result = PageResult.Stay();

        Assert.Equal(PageResultKind.Stay, result.Kind);
        Assert.Null(result.Message);
    }

    [Fact]
    public void Stay_with_message_preserves_kind_and_message()
    {
        var result = PageResult.Stay("Validation failed");

        Assert.Equal(PageResultKind.Stay, result.Kind);
        Assert.Equal("Validation failed", result.Message);
    }

    [Fact]
    public void Stay_creates_new_instance_each_call()
    {
        var first = PageResult.Stay();
        var second = PageResult.Stay();

        Assert.NotSame(first, second);
    }

    [Fact]
    public void GoTo_has_correct_kind_and_target_type()
    {
        var result = PageResult.GoTo<DummyPage>();

        Assert.Equal(PageResultKind.GoTo, result.Kind);
        Assert.Null(result.Message);
        Assert.Equal(typeof(DummyPage), result.TargetType);
    }

    [Fact]
    public void GoTo_creates_new_instance_each_call()
    {
        var first = PageResult.GoTo<DummyPage>();
        var second = PageResult.GoTo<DummyPage>();

        Assert.NotSame(first, second);
    }

    private static PageResult GetSingletonByName(string fieldName) =>
        fieldName switch
        {
            nameof(PageResult.Next) => PageResult.Next,
            nameof(PageResult.Previous) => PageResult.Previous,
            nameof(PageResult.Finish) => PageResult.Finish,
            nameof(PageResult.Cancel) => PageResult.Cancel,
            nameof(PageResult.Install) => PageResult.Install,
            nameof(PageResult.Uninstall) => PageResult.Uninstall,
            nameof(PageResult.Repair) => PageResult.Repair,
            _ => throw new ArgumentException($"Unknown singleton: {fieldName}")
        };

#pragma warning disable CA1812 // Used only as the generic type argument to PageResult.GoTo<TPage>(); never instantiated by design
    private sealed class DummyPage;
#pragma warning restore CA1812
}
