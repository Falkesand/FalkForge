namespace FalkForge.Ui.Abstractions.Tests;

using Xunit;

public sealed class PageResultTests
{
    [Fact]
    public void Next_returns_same_instance_on_repeated_access()
    {
        var first = PageResult.Next;
        var second = PageResult.Next;

        Assert.Same(first, second);
    }

    [Fact]
    public void Previous_returns_same_instance_on_repeated_access()
    {
        var first = PageResult.Previous;
        var second = PageResult.Previous;

        Assert.Same(first, second);
    }

    [Fact]
    public void Finish_returns_same_instance_on_repeated_access()
    {
        var first = PageResult.Finish;
        var second = PageResult.Finish;

        Assert.Same(first, second);
    }

    [Fact]
    public void Cancel_returns_same_instance_on_repeated_access()
    {
        var first = PageResult.Cancel;
        var second = PageResult.Cancel;

        Assert.Same(first, second);
    }

    [Fact]
    public void Install_returns_same_instance_on_repeated_access()
    {
        var first = PageResult.Install;
        var second = PageResult.Install;

        Assert.Same(first, second);
    }

    [Fact]
    public void Uninstall_returns_same_instance_on_repeated_access()
    {
        var first = PageResult.Uninstall;
        var second = PageResult.Uninstall;

        Assert.Same(first, second);
    }

    [Fact]
    public void Repair_returns_same_instance_on_repeated_access()
    {
        var first = PageResult.Repair;
        var second = PageResult.Repair;

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

    [Theory]
    [InlineData(nameof(PageResult.Next))]
    [InlineData(nameof(PageResult.Previous))]
    [InlineData(nameof(PageResult.Finish))]
    [InlineData(nameof(PageResult.Cancel))]
    [InlineData(nameof(PageResult.Install))]
    [InlineData(nameof(PageResult.Uninstall))]
    [InlineData(nameof(PageResult.Repair))]
    public void Singleton_has_null_message(string fieldName)
    {
        var result = GetSingletonByName(fieldName);

        Assert.Null(result.Message);
    }

    [Theory]
    [InlineData(nameof(PageResult.Next))]
    [InlineData(nameof(PageResult.Previous))]
    [InlineData(nameof(PageResult.Finish))]
    [InlineData(nameof(PageResult.Cancel))]
    [InlineData(nameof(PageResult.Install))]
    [InlineData(nameof(PageResult.Uninstall))]
    [InlineData(nameof(PageResult.Repair))]
    public void Singleton_has_null_target_type(string fieldName)
    {
        var result = GetSingletonByName(fieldName);

        Assert.Null(result.TargetType);
    }

    [Fact]
    public void Stay_without_message_has_kind_stay()
    {
        var result = PageResult.Stay();

        Assert.Equal(PageResultKind.Stay, result.Kind);
    }

    [Fact]
    public void Stay_without_message_has_null_message()
    {
        var result = PageResult.Stay();

        Assert.Null(result.Message);
    }

    [Fact]
    public void Stay_with_message_has_kind_stay()
    {
        var result = PageResult.Stay("Validation failed");

        Assert.Equal(PageResultKind.Stay, result.Kind);
    }

    [Fact]
    public void Stay_with_message_preserves_message()
    {
        var result = PageResult.Stay("Validation failed");

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
    public void GoTo_has_kind_goto()
    {
        var result = PageResult.GoTo<DummyPage>();

        Assert.Equal(PageResultKind.GoTo, result.Kind);
    }

    [Fact]
    public void GoTo_has_null_message()
    {
        var result = PageResult.GoTo<DummyPage>();

        Assert.Null(result.Message);
    }

    [Fact]
    public void GoTo_has_correct_target_type()
    {
        var result = PageResult.GoTo<DummyPage>();

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

    private sealed class DummyPage;
}
