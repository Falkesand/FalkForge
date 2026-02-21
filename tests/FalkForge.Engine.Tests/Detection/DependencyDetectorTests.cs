namespace FalkForge.Engine.Tests.Detection;

using FalkForge.Engine.Detection;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using Xunit;

public sealed class DependencyDetectorTests
{
    [Fact]
    public void DetectBlockingDependencies_NoProviders_ReturnsEmpty()
    {
        var registry = new MockRegistry();

        var result = DependencyDetector.DetectBlockingDependencies([], registry);

        Assert.Empty(result);
    }

    [Fact]
    public void DetectBlockingDependencies_NoDependents_ReturnsEmpty()
    {
        var registry = new MockRegistry();
        // Provider key exists but no Dependents subkey
        registry.SetStringValue("HKLM",
            @"SOFTWARE\Classes\Installer\Dependencies\MyApp",
            "Version", "1.0.0");

        var providers = new[]
        {
            new ManifestDependencyProvider("MyApp", "1.0.0", "My Application")
        };

        var result = DependencyDetector.DetectBlockingDependencies(providers, registry);

        Assert.Empty(result);
    }

    [Fact]
    public void DetectBlockingDependencies_WithDependents_ReturnsBlocker()
    {
        var registry = new MockRegistry();
        registry.SetStringValue("HKLM",
            @"SOFTWARE\Classes\Installer\Dependencies\MyApp",
            "Version", "1.0.0");
        // Add the Dependents key and a dependent subkey
        registry.AddKey("HKLM",
            @"SOFTWARE\Classes\Installer\Dependencies\MyApp\Dependents");
        registry.AddKey("HKLM",
            @"SOFTWARE\Classes\Installer\Dependencies\MyApp\Dependents\OtherApp");

        var providers = new[]
        {
            new ManifestDependencyProvider("MyApp", "1.0.0", "My Application")
        };

        var result = DependencyDetector.DetectBlockingDependencies(providers, registry);

        Assert.Single(result);
        Assert.Equal("MyApp", result[0].ProviderKey);
        Assert.Equal("My Application", result[0].DisplayName);
        Assert.Single(result[0].DependentKeys);
        Assert.Equal("OtherApp", result[0].DependentKeys[0]);
    }

    [Fact]
    public void DetectBlockingDependencies_MultipleDependents_ReturnsAll()
    {
        var registry = new MockRegistry();
        registry.AddKey("HKLM",
            @"SOFTWARE\Classes\Installer\Dependencies\SharedLib\Dependents");
        registry.AddKey("HKLM",
            @"SOFTWARE\Classes\Installer\Dependencies\SharedLib\Dependents\AppA");
        registry.AddKey("HKLM",
            @"SOFTWARE\Classes\Installer\Dependencies\SharedLib\Dependents\AppB");
        registry.AddKey("HKLM",
            @"SOFTWARE\Classes\Installer\Dependencies\SharedLib\Dependents\AppC");

        var providers = new[]
        {
            new ManifestDependencyProvider("SharedLib", "2.0.0", "Shared Library")
        };

        var result = DependencyDetector.DetectBlockingDependencies(providers, registry);

        Assert.Single(result);
        Assert.Equal("SharedLib", result[0].ProviderKey);
        Assert.Equal(3, result[0].DependentKeys.Count);
        Assert.Contains("AppA", result[0].DependentKeys);
        Assert.Contains("AppB", result[0].DependentKeys);
        Assert.Contains("AppC", result[0].DependentKeys);
    }

    [Fact]
    public void DetectBlockingDependencies_MultipleProviders_OnlyBlockedReturned()
    {
        var registry = new MockRegistry();
        // First provider has a dependent
        registry.AddKey("HKLM",
            @"SOFTWARE\Classes\Installer\Dependencies\ProviderA\Dependents");
        registry.AddKey("HKLM",
            @"SOFTWARE\Classes\Installer\Dependencies\ProviderA\Dependents\ConsumerX");
        // Second provider has no Dependents key at all

        var providers = new[]
        {
            new ManifestDependencyProvider("ProviderA", "1.0.0", "Provider A"),
            new ManifestDependencyProvider("ProviderB", "1.0.0", "Provider B")
        };

        var result = DependencyDetector.DetectBlockingDependencies(providers, registry);

        Assert.Single(result);
        Assert.Equal("ProviderA", result[0].ProviderKey);
    }

    [Fact]
    public void DetectBlockingDependencies_DependentsKeyExists_ButEmpty_ReturnsEmpty()
    {
        var registry = new MockRegistry();
        // Create the Dependents key with no subkeys (just a value to make it exist)
        registry.AddKey("HKLM",
            @"SOFTWARE\Classes\Installer\Dependencies\MyApp\Dependents");

        var providers = new[]
        {
            new ManifestDependencyProvider("MyApp", "1.0.0", "My Application")
        };

        var result = DependencyDetector.DetectBlockingDependencies(providers, registry);

        Assert.Empty(result);
    }
}
