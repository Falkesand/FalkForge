namespace FalkForge.Extensibility.Tests;

using FalkForge.Extensibility;
using Xunit;

public sealed class ExtensionRegistrationTests
{
    private sealed class StubExtension : IFalkForgeExtension
    {
        public StubExtension(string name, string? version = null, string? minHostVersion = null)
        {
            Name = name;
            _version = version;
            _minHostVersion = minHostVersion;
        }

        private readonly string? _version;
        private readonly string? _minHostVersion;

        public string Name { get; }
        public string Version => _version ?? "1.0.0";
        public string? MinHostVersion => _minHostVersion;
        public int RegisterCallCount { get; private set; }

        public void Register(IExtensionRegistry registry) => RegisterCallCount++;
    }

    private sealed class NullRegistry : IExtensionRegistry
    {
        public void RegisterTableContributor(IMsiTableContributor contributor) { }
        public void RegisterComponentContributor(IComponentContributor contributor) { }
        public void RegisterValidator(IExtensionValidator validator) { }
        public void RegisterDryRunContributor(IDryRunContributor contributor) { }
        public void RegisterDialogStep(IDialogStepBuilder builder) { }
    }

    [Fact]
    public void Register_FirstExtension_AddsNameToRegisteredSetAndCallsRegister()
    {
        var ext = new StubExtension("Util");
        var registered = new HashSet<string>(StringComparer.Ordinal);

        ExtensionRegistration.Register(ext, new NullRegistry(), registered);

        Assert.Contains("Util", registered);
        Assert.Equal(1, ext.RegisterCallCount);
    }

    [Fact]
    public void Register_DuplicateName_ThrowsPluginCompatibilityException()
    {
        var first = new StubExtension("Sql", version: "1.0.0");
        var second = new StubExtension("Sql", version: "2.0.0");
        var registered = new HashSet<string>(StringComparer.Ordinal);

        ExtensionRegistration.Register(first, new NullRegistry(), registered);

        var ex = Assert.Throws<PluginCompatibilityException>(
            () => ExtensionRegistration.Register(second, new NullRegistry(), registered));

        Assert.Contains("Sql", ex.Message);
        Assert.Contains("already been registered", ex.Message);
        Assert.Equal(0, second.RegisterCallCount);
    }

    [Fact]
    public void Register_MinHostVersionExceedsHost_ThrowsPluginCompatibilityException()
    {
        var ext = new StubExtension("Future", version: "1.0.0", minHostVersion: "2.0.0");
        var registered = new HashSet<string>(StringComparer.Ordinal);

        var ex = Assert.Throws<PluginCompatibilityException>(
            () => ExtensionRegistration.Register(ext, new NullRegistry(), registered, hostVersion: "1.0.0"));

        Assert.Contains("Future", ex.Message);
        Assert.Contains("requires host version", ex.Message);
        Assert.Equal(0, ext.RegisterCallCount);
        Assert.DoesNotContain("Future", registered);
    }

    [Fact]
    public void Register_MinHostVersionSatisfied_Succeeds()
    {
        var ext = new StubExtension("Net", version: "1.0.0", minHostVersion: "1.0.0");
        var registered = new HashSet<string>(StringComparer.Ordinal);

        ExtensionRegistration.Register(ext, new NullRegistry(), registered, hostVersion: "1.5.0");

        Assert.Equal(1, ext.RegisterCallCount);
        Assert.Contains("Net", registered);
    }

    [Fact]
    public void Register_EmptyName_Throws()
    {
        var ext = new StubExtension("");
        var registered = new HashSet<string>(StringComparer.Ordinal);

        var ex = Assert.Throws<PluginCompatibilityException>(
            () => ExtensionRegistration.Register(ext, new NullRegistry(), registered));

        Assert.Contains("empty Name", ex.Message);
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0.0", "1.0.1", -1)]
    [InlineData("2.0.0", "1.99.99", 1)]
    [InlineData("1", "1.0.0", 0)]
    [InlineData("1.0", "1.0.0", 0)]
    [InlineData("1.0.0-rc1", "1.0.0", 0)]
    public void CompareSemVer_ProducesExpectedOrdering(string left, string right, int expectedSign)
    {
        var actual = ExtensionRegistration.CompareSemVer(left, right);
        Assert.Equal(expectedSign, Math.Sign(actual));
    }

    [Fact]
    public void DefaultExtensionVersion_IsOnePointZero()
    {
        var ext = new StubExtension("Default");
        Assert.Equal("1.0.0", ext.Version);
        Assert.Null(ext.MinHostVersion);
    }
}
