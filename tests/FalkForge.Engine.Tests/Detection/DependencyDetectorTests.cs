namespace FalkForge.Engine.Tests.Detection;

using FalkForge.Engine.Detection;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using FalkForge.Testing;
using Xunit;

public sealed class DependencyDetectorTests
{
    [Fact]
    public void DetectBlockingDependencies_NoProviders_ReturnsEmpty()
    {
        var registry = new MockRegistry();

        var result = DependencyDetector.DetectBlockingDependencies([], registry);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void DetectBlockingDependencies_NoDependents_ReturnsEmpty()
    {
        var registry = new MockRegistry();
        // Provider key exists but no Dependents subkey
        registry.SetStringValue(RegistryRoot.LocalMachine,
            @"SOFTWARE\Classes\Installer\Dependencies\MyApp",
            "Version", "1.0.0");

        var providers = new[]
        {
            new ManifestDependencyProvider("MyApp", "1.0.0", "My Application")
        };

        var result = DependencyDetector.DetectBlockingDependencies(providers, registry);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void DetectBlockingDependencies_WithDependents_ReturnsBlocker()
    {
        var registry = new MockRegistry();
        registry.SetStringValue(RegistryRoot.LocalMachine,
            @"SOFTWARE\Classes\Installer\Dependencies\MyApp",
            "Version", "1.0.0");
        // Add the Dependents key and a dependent subkey
        registry.AddKey(RegistryRoot.LocalMachine,
            @"SOFTWARE\Classes\Installer\Dependencies\MyApp\Dependents");
        registry.AddKey(RegistryRoot.LocalMachine,
            @"SOFTWARE\Classes\Installer\Dependencies\MyApp\Dependents\OtherApp");

        var providers = new[]
        {
            new ManifestDependencyProvider("MyApp", "1.0.0", "My Application")
        };

        var result = DependencyDetector.DetectBlockingDependencies(providers, registry);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("MyApp", result.Value[0].ProviderKey);
        Assert.Equal("My Application", result.Value[0].DisplayName);
        Assert.Single(result.Value[0].DependentKeys);
        Assert.Equal("OtherApp", result.Value[0].DependentKeys[0]);
    }

    [Fact]
    public void DetectBlockingDependencies_MultipleDependents_ReturnsAll()
    {
        var registry = new MockRegistry();
        registry.AddKey(RegistryRoot.LocalMachine,
            @"SOFTWARE\Classes\Installer\Dependencies\SharedLib\Dependents");
        registry.AddKey(RegistryRoot.LocalMachine,
            @"SOFTWARE\Classes\Installer\Dependencies\SharedLib\Dependents\AppA");
        registry.AddKey(RegistryRoot.LocalMachine,
            @"SOFTWARE\Classes\Installer\Dependencies\SharedLib\Dependents\AppB");
        registry.AddKey(RegistryRoot.LocalMachine,
            @"SOFTWARE\Classes\Installer\Dependencies\SharedLib\Dependents\AppC");

        var providers = new[]
        {
            new ManifestDependencyProvider("SharedLib", "2.0.0", "Shared Library")
        };

        var result = DependencyDetector.DetectBlockingDependencies(providers, registry);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("SharedLib", result.Value[0].ProviderKey);
        Assert.Equal(3, result.Value[0].DependentKeys.Count);
        Assert.Contains("AppA", result.Value[0].DependentKeys);
        Assert.Contains("AppB", result.Value[0].DependentKeys);
        Assert.Contains("AppC", result.Value[0].DependentKeys);
    }

    [Fact]
    public void DetectBlockingDependencies_MultipleProviders_OnlyBlockedReturned()
    {
        var registry = new MockRegistry();
        // First provider has a dependent
        registry.AddKey(RegistryRoot.LocalMachine,
            @"SOFTWARE\Classes\Installer\Dependencies\ProviderA\Dependents");
        registry.AddKey(RegistryRoot.LocalMachine,
            @"SOFTWARE\Classes\Installer\Dependencies\ProviderA\Dependents\ConsumerX");
        // Second provider has no Dependents key at all

        var providers = new[]
        {
            new ManifestDependencyProvider("ProviderA", "1.0.0", "Provider A"),
            new ManifestDependencyProvider("ProviderB", "1.0.0", "Provider B")
        };

        var result = DependencyDetector.DetectBlockingDependencies(providers, registry);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("ProviderA", result.Value[0].ProviderKey);
    }

    [Fact]
    public void DetectBlockingDependencies_DependentsKeyExists_ButEmpty_ReturnsEmpty()
    {
        var registry = new MockRegistry();
        // Create the Dependents key with no subkeys (just a value to make it exist)
        registry.AddKey(RegistryRoot.LocalMachine,
            @"SOFTWARE\Classes\Installer\Dependencies\MyApp\Dependents");

        var providers = new[]
        {
            new ManifestDependencyProvider("MyApp", "1.0.0", "My Application")
        };

        var result = DependencyDetector.DetectBlockingDependencies(providers, registry);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void DetectBlockingDependencies_DependentRegisteredUnderCurrentUser_IsFoundToo()
    {
        // A per-user consumer of a per-machine provider is a real shape: the provider row lives
        // under LocalMachine but its dependent registered itself under CurrentUser. The uninstall
        // check must union-read both roots, not just the provider's own scope.
        var registry = new MockRegistry();
        registry.SetStringValue(RegistryRoot.LocalMachine,
            @"SOFTWARE\Classes\Installer\Dependencies\MyApp",
            "Version", "1.0.0");
        registry.AddKey(RegistryRoot.CurrentUser,
            @"SOFTWARE\Classes\Installer\Dependencies\MyApp\Dependents");
        registry.AddKey(RegistryRoot.CurrentUser,
            @"SOFTWARE\Classes\Installer\Dependencies\MyApp\Dependents\PerUserConsumer");

        var providers = new[]
        {
            new ManifestDependencyProvider("MyApp", "1.0.0", "My Application")
        };

        var result = DependencyDetector.DetectBlockingDependencies(providers, registry);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Contains("PerUserConsumer", result.Value[0].DependentKeys);
    }

    [Fact]
    public void DetectBlockingDependencies_DependentsInBothRoots_UnionsAllDependentKeys()
    {
        var registry = new MockRegistry();
        registry.AddKey(RegistryRoot.LocalMachine,
            @"SOFTWARE\Classes\Installer\Dependencies\MyApp\Dependents");
        registry.AddKey(RegistryRoot.LocalMachine,
            @"SOFTWARE\Classes\Installer\Dependencies\MyApp\Dependents\MachineConsumer");
        registry.AddKey(RegistryRoot.CurrentUser,
            @"SOFTWARE\Classes\Installer\Dependencies\MyApp\Dependents");
        registry.AddKey(RegistryRoot.CurrentUser,
            @"SOFTWARE\Classes\Installer\Dependencies\MyApp\Dependents\UserConsumer");

        var providers = new[]
        {
            new ManifestDependencyProvider("MyApp", "1.0.0", "My Application")
        };

        var result = DependencyDetector.DetectBlockingDependencies(providers, registry);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal(2, result.Value[0].DependentKeys.Count);
        Assert.Contains("MachineConsumer", result.Value[0].DependentKeys);
        Assert.Contains("UserConsumer", result.Value[0].DependentKeys);
    }

    [Fact]
    public void DetectBlockingDependencies_ReadFailureOnEitherRoot_FailsClosed()
    {
        // This is the defect class being fixed: an inconclusive read (access denied / unreadable)
        // must NEVER silently become "no dependants" — that would let an uninstall through that
        // should have been blocked. A read failure propagates as a Result failure instead.
        var registry = new MockRegistry();
        registry.FailReadsUnder(@"SOFTWARE\Classes\Installer\Dependencies");

        var providers = new[]
        {
            new ManifestDependencyProvider("MyApp", "1.0.0", "My Application")
        };

        var result = DependencyDetector.DetectBlockingDependencies(providers, registry);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void DetectUnsatisfiedProviders_NoRequirements_ReturnsEmpty()
    {
        var registry = new MockRegistry();

        var result = DependencyDetector.DetectUnsatisfiedProviders([], registry);

        Assert.Empty(result);
    }

    [Fact]
    public void DetectUnsatisfiedProviders_ProviderMissing_ReturnsUnsatisfied()
    {
        var registry = new MockRegistry();
        var requirements = new[]
        {
            new ManifestDependencyRequirement("MissingLib", "1.0.0", null, true, false)
        };

        var result = DependencyDetector.DetectUnsatisfiedProviders(requirements, registry);

        Assert.Single(result);
        Assert.Equal("MissingLib", result[0].ProviderKey);
        Assert.True(result[0].IsMissing);
        Assert.Null(result[0].InstalledVersion);
    }

    [Fact]
    public void DetectUnsatisfiedProviders_VersionTooLow_ReturnsUnsatisfied()
    {
        var registry = new MockRegistry();
        registry.SetStringValue(RegistryRoot.LocalMachine,
            @"SOFTWARE\Classes\Installer\Dependencies\SharedLib",
            "Version", "1.0.0");

        var requirements = new[]
        {
            new ManifestDependencyRequirement("SharedLib", "2.0.0", null, true, false)
        };

        var result = DependencyDetector.DetectUnsatisfiedProviders(requirements, registry);

        Assert.Single(result);
        Assert.Equal("SharedLib", result[0].ProviderKey);
        Assert.False(result[0].IsMissing);
        Assert.Equal("1.0.0", result[0].InstalledVersion);
    }

    [Fact]
    public void DetectUnsatisfiedProviders_VersionSatisfied_ReturnsEmpty()
    {
        var registry = new MockRegistry();
        registry.SetStringValue(RegistryRoot.LocalMachine,
            @"SOFTWARE\Classes\Installer\Dependencies\SharedLib",
            "Version", "2.5.0");

        var requirements = new[]
        {
            new ManifestDependencyRequirement("SharedLib", "2.0.0", "3.0.0", true, false)
        };

        var result = DependencyDetector.DetectUnsatisfiedProviders(requirements, registry);

        Assert.Empty(result);
    }

    [Fact]
    public void DetectUnsatisfiedProviders_VersionAtMaxExclusive_ReturnsUnsatisfied()
    {
        var registry = new MockRegistry();
        registry.SetStringValue(RegistryRoot.LocalMachine,
            @"SOFTWARE\Classes\Installer\Dependencies\SharedLib",
            "Version", "3.0.0");

        var requirements = new[]
        {
            new ManifestDependencyRequirement("SharedLib", "2.0.0", "3.0.0", true, false)
        };

        var result = DependencyDetector.DetectUnsatisfiedProviders(requirements, registry);

        Assert.Single(result);
    }

    [Fact]
    public void DetectUnsatisfiedProviders_VersionAtMaxInclusive_ReturnsEmpty()
    {
        var registry = new MockRegistry();
        registry.SetStringValue(RegistryRoot.LocalMachine,
            @"SOFTWARE\Classes\Installer\Dependencies\SharedLib",
            "Version", "3.0.0");

        var requirements = new[]
        {
            new ManifestDependencyRequirement("SharedLib", "2.0.0", "3.0.0", true, true)
        };

        var result = DependencyDetector.DetectUnsatisfiedProviders(requirements, registry);

        Assert.Empty(result);
    }

    [Fact]
    public void DetectUnsatisfiedProviders_ProviderRegisteredUnderCurrentUser_IsFoundToo()
    {
        // Union read: a per-user provider satisfies a requirement even though the check's "default"
        // root is LocalMachine — a per-machine bundle can depend on a per-user-installed component.
        var registry = new MockRegistry();
        registry.SetStringValue(RegistryRoot.CurrentUser,
            @"SOFTWARE\Classes\Installer\Dependencies\SharedLib",
            "Version", "2.0.0");

        var requirements = new[]
        {
            new ManifestDependencyRequirement("SharedLib", "1.0.0", null, true, false)
        };

        var result = DependencyDetector.DetectUnsatisfiedProviders(requirements, registry);

        Assert.Empty(result);
    }

    [Fact]
    public void DetectUnsatisfiedProviders_UnparseableMinVersion_TreatedAsUnsatisfied()
    {
        // Latent hole: previously `if (Version.TryParse(req.MinVersion, out var min))` guarded the
        // whole comparison, so a typo'd bound (e.g. "not-a-version") silently skipped the check and
        // the requirement passed regardless of what was installed. An unparseable bound must now
        // fail the requirement instead of vanishing.
        var registry = new MockRegistry();
        registry.SetStringValue(RegistryRoot.LocalMachine,
            @"SOFTWARE\Classes\Installer\Dependencies\SharedLib",
            "Version", "5.0.0");

        var requirements = new[]
        {
            new ManifestDependencyRequirement("SharedLib", "not-a-version", null, true, false)
        };

        var result = DependencyDetector.DetectUnsatisfiedProviders(requirements, registry);

        Assert.Single(result);
        Assert.Equal("SharedLib", result[0].ProviderKey);
    }

    [Fact]
    public void DetectUnsatisfiedProviders_UnparseableMaxVersion_TreatedAsUnsatisfied()
    {
        var registry = new MockRegistry();
        registry.SetStringValue(RegistryRoot.LocalMachine,
            @"SOFTWARE\Classes\Installer\Dependencies\SharedLib",
            "Version", "1.0.0");

        var requirements = new[]
        {
            new ManifestDependencyRequirement("SharedLib", null, "not-a-version", true, false)
        };

        var result = DependencyDetector.DetectUnsatisfiedProviders(requirements, registry);

        Assert.Single(result);
        Assert.Equal("SharedLib", result[0].ProviderKey);
    }
}
