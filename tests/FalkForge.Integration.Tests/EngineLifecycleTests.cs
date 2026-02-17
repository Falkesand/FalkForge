using FalkForge.Engine;
using FalkForge.Engine.Detection;
using FalkForge.Engine.Journal;
using FalkForge.Engine.Journal.UndoOperations;
using FalkForge.Engine.Phases;
using FalkForge.Engine.Planning;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Platform;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Integration.Tests;

public sealed class EngineLifecycleTests
{
    [Fact]
    public async Task FullLifecycle_PerUserInstall_AllPhasesVisited()
    {
        var visitedPhases = new List<EnginePhase>();
        var manifest = CreateTestManifest(InstallScope.PerUser);
        var mockPlatform = CreateMockPlatform();

        var detector = new PackageDetector(mockPlatform.Registry);
        var planner = new Planner();

        var handlers = new IEnginePhaseHandler[]
        {
            new TrackingHandler(EnginePhase.Initializing, visitedPhases,
                new InitializingHandler()),
            new TrackingHandler(EnginePhase.Detecting, visitedPhases,
                new DetectingHandler(detector)),
            new TrackingHandler(EnginePhase.Planning, visitedPhases,
                new PlanningHandler(planner)),
            new TrackingHandler(EnginePhase.Applying, visitedPhases,
                new SuccessApplyHandler()),
            new TrackingHandler(EnginePhase.Completing, visitedPhases,
                new CompletingHandler()),
        };

        var sm = new EngineStateMachine(handlers);
        var context = new EngineContext
        {
            Manifest = manifest,
            Platform = mockPlatform,
            UiPipe = null,
            ShutdownToken = CancellationToken.None,
            RequestedAction = InstallAction.Install
        };

        var exitCode = await sm.RunAsync(context, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(EnginePhase.Shutdown, sm.CurrentPhase);
        Assert.Equal(
        [
            EnginePhase.Initializing,
            EnginePhase.Detecting,
            EnginePhase.Planning,
            EnginePhase.Applying,
            EnginePhase.Completing
        ], visitedPhases);
    }

    [Fact]
    public async Task FullLifecycle_PerMachineInstall_IncludesElevation()
    {
        var visitedPhases = new List<EnginePhase>();
        var manifest = CreateTestManifest(InstallScope.PerMachine);
        var mockPlatform = CreateMockPlatform();

        var detector = new PackageDetector(mockPlatform.Registry);
        var planner = new Planner();

        var handlers = new IEnginePhaseHandler[]
        {
            new TrackingHandler(EnginePhase.Initializing, visitedPhases,
                new InitializingHandler()),
            new TrackingHandler(EnginePhase.Detecting, visitedPhases,
                new DetectingHandler(detector)),
            new TrackingHandler(EnginePhase.Planning, visitedPhases,
                new PlanningHandler(planner)),
            new TrackingHandler(EnginePhase.Elevating, visitedPhases,
                new StubElevatingHandler()),
            new TrackingHandler(EnginePhase.Applying, visitedPhases,
                new SuccessApplyHandler()),
            new TrackingHandler(EnginePhase.Completing, visitedPhases,
                new CompletingHandler()),
        };

        var sm = new EngineStateMachine(handlers);
        var context = new EngineContext
        {
            Manifest = manifest,
            Platform = mockPlatform,
            UiPipe = null,
            ShutdownToken = CancellationToken.None,
            RequestedAction = InstallAction.Install
        };

        var exitCode = await sm.RunAsync(context, CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(EnginePhase.Shutdown, sm.CurrentPhase);
        Assert.Contains(EnginePhase.Elevating, visitedPhases);
        Assert.Equal(
        [
            EnginePhase.Initializing,
            EnginePhase.Detecting,
            EnginePhase.Planning,
            EnginePhase.Elevating,
            EnginePhase.Applying,
            EnginePhase.Completing
        ], visitedPhases);
    }

    [Fact]
    public async Task FullLifecycle_DetectionFindsInstalledPackage_StateReflected()
    {
        var visitedPhases = new List<EnginePhase>();
        var productCode = "{12345678-1234-1234-1234-123456789012}";
        var manifest = CreateTestManifest(InstallScope.PerUser, productCode);

        var registry = new MockRegistry();
        registry.AddKey("HKLM", $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{productCode}");
        registry.SetStringValue("HKLM", $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{productCode}",
            "DisplayVersion", "1.0.0");

        var mockPlatform = CreateMockPlatform(registry);
        var detector = new PackageDetector(mockPlatform.Registry);
        var planner = new Planner();

        var handlers = new IEnginePhaseHandler[]
        {
            new TrackingHandler(EnginePhase.Initializing, visitedPhases,
                new InitializingHandler()),
            new TrackingHandler(EnginePhase.Detecting, visitedPhases,
                new DetectingHandler(detector)),
            new TrackingHandler(EnginePhase.Planning, visitedPhases,
                new PlanningHandler(planner)),
            new TrackingHandler(EnginePhase.Applying, visitedPhases,
                new SuccessApplyHandler()),
            new TrackingHandler(EnginePhase.Completing, visitedPhases,
                new CompletingHandler()),
        };

        var sm = new EngineStateMachine(handlers);
        var context = new EngineContext
        {
            Manifest = manifest,
            Platform = mockPlatform,
            UiPipe = null,
            ShutdownToken = CancellationToken.None,
            RequestedAction = InstallAction.Install
        };

        await sm.RunAsync(context, CancellationToken.None);

        Assert.Equal(InstallState.Installed, context.DetectedState);
        Assert.Equal("1.0.0", context.DetectedVersion);
    }

    [Fact]
    public async Task FullLifecycle_ApplyFailure_TransitionsToFailedThenShutdown()
    {
        var visitedPhases = new List<EnginePhase>();
        var manifest = CreateTestManifest(InstallScope.PerUser);
        var mockPlatform = CreateMockPlatform();

        var detector = new PackageDetector(mockPlatform.Registry);
        var planner = new Planner();

        var handlers = new IEnginePhaseHandler[]
        {
            new TrackingHandler(EnginePhase.Initializing, visitedPhases,
                new InitializingHandler()),
            new TrackingHandler(EnginePhase.Detecting, visitedPhases,
                new DetectingHandler(detector)),
            new TrackingHandler(EnginePhase.Planning, visitedPhases,
                new PlanningHandler(planner)),
            new TrackingHandler(EnginePhase.Applying, visitedPhases,
                new FailingApplyHandler()),
            new TrackingHandler(EnginePhase.Failed, visitedPhases,
                new FailedHandler()),
            new TrackingHandler(EnginePhase.RollingBack, visitedPhases,
                new RollingBackHandler(new RollbackExecutor(Array.Empty<IUndoOperation>()))),
        };

        var sm = new EngineStateMachine(handlers);
        var context = new EngineContext
        {
            Manifest = manifest,
            Platform = mockPlatform,
            UiPipe = null,
            ShutdownToken = CancellationToken.None,
            RequestedAction = InstallAction.Install
        };

        var exitCode = await sm.RunAsync(context, CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Equal(EnginePhase.Shutdown, sm.CurrentPhase);
        Assert.Contains(EnginePhase.Failed, visitedPhases);
    }

    [Fact]
    public async Task FullLifecycle_InstallDirectory_SetFromManifest()
    {
        var manifest = CreateTestManifest(InstallScope.PerUser);
        var mockPlatform = CreateMockPlatform();

        var detector = new PackageDetector(mockPlatform.Registry);
        var planner = new Planner();

        var handlers = new IEnginePhaseHandler[]
        {
            new InitializingHandler(),
            new DetectingHandler(detector),
            new PlanningHandler(planner),
            new SuccessApplyHandler(),
            new CompletingHandler(),
        };

        var sm = new EngineStateMachine(handlers);
        var context = new EngineContext
        {
            Manifest = manifest,
            Platform = mockPlatform,
            UiPipe = null,
            ShutdownToken = CancellationToken.None,
            RequestedAction = InstallAction.Install
        };

        await sm.RunAsync(context, CancellationToken.None);

        Assert.Equal(@"C:\Users\Test\AppData\Local\TestApp", context.InstallDirectory);
    }

    [Fact]
    public async Task FullLifecycle_PlanContainsCorrectPackages()
    {
        var manifest = CreateTestManifest(InstallScope.PerUser);
        var mockPlatform = CreateMockPlatform();

        var detector = new PackageDetector(mockPlatform.Registry);
        var planner = new Planner();

        var handlers = new IEnginePhaseHandler[]
        {
            new InitializingHandler(),
            new DetectingHandler(detector),
            new PlanningHandler(planner),
            new SuccessApplyHandler(),
            new CompletingHandler(),
        };

        var sm = new EngineStateMachine(handlers);
        var context = new EngineContext
        {
            Manifest = manifest,
            Platform = mockPlatform,
            UiPipe = null,
            ShutdownToken = CancellationToken.None,
            RequestedAction = InstallAction.Install
        };

        await sm.RunAsync(context, CancellationToken.None);

        Assert.NotNull(context.CurrentPlan);
        Assert.Single(context.CurrentPlan.Actions);
        Assert.Equal("TestMsi", context.CurrentPlan.Actions[0].PackageId);
        Assert.Equal(PlanActionType.Install, context.CurrentPlan.Actions[0].ActionType);
    }

    private static InstallerManifest CreateTestManifest(
        InstallScope scope,
        string? productCode = null)
    {
        var props = new Dictionary<string, string>();
        if (productCode is not null)
            props["ProductCode"] = productCode;

        return new InstallerManifest
        {
            Name = "TestApp",
            Manufacturer = "Test Manufacturer",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = scope,
            Packages =
            [
                new PackageInfo
                {
                    Id = "TestMsi",
                    Type = PackageType.MsiPackage,
                    DisplayName = "Test MSI",
                    SourcePath = @"C:\test\TestMsi.msi",
                    Sha256Hash = "AABBCCDD",
                    Properties = props
                }
            ]
        };
    }

    private static MockPlatformServices CreateMockPlatform(MockRegistry? registry = null)
    {
        var env = new MockEnvironment()
            .SetFolderPath(Environment.SpecialFolder.LocalApplicationData, @"C:\Users\Test\AppData\Local")
            .SetFolderPath(Environment.SpecialFolder.ProgramFiles, @"C:\Program Files");

        return new MockPlatformServices(
            registry: registry ?? new MockRegistry(),
            environment: env);
    }

    /// <summary>
    /// Wraps a real handler to track phase visits.
    /// </summary>
    private sealed class TrackingHandler : IEnginePhaseHandler
    {
        private readonly List<EnginePhase> _visited;
        private readonly IEnginePhaseHandler _inner;

        public TrackingHandler(EnginePhase phase, List<EnginePhase> visited, IEnginePhaseHandler inner)
        {
            Phase = phase;
            _visited = visited;
            _inner = inner;
        }

        public EnginePhase Phase { get; }

        public Task<EnginePhase> ExecuteAsync(EngineContext context, CancellationToken ct)
        {
            _visited.Add(Phase);
            return _inner.ExecuteAsync(context, ct);
        }
    }

    /// <summary>
    /// Stub handler for the Elevating phase that skips real elevation.
    /// </summary>
    private sealed class StubElevatingHandler : IEnginePhaseHandler
    {
        public EnginePhase Phase => EnginePhase.Elevating;

        public Task<EnginePhase> ExecuteAsync(EngineContext context, CancellationToken ct)
        {
            return Task.FromResult(EnginePhase.Applying);
        }
    }

    /// <summary>
    /// Stub handler for the Applying phase that simulates a successful apply.
    /// </summary>
    private sealed class SuccessApplyHandler : IEnginePhaseHandler
    {
        public EnginePhase Phase => EnginePhase.Applying;

        public Task<EnginePhase> ExecuteAsync(EngineContext context, CancellationToken ct)
        {
            return Task.FromResult(EnginePhase.Completing);
        }
    }

    /// <summary>
    /// Stub handler for the Applying phase that simulates a failure.
    /// </summary>
    private sealed class FailingApplyHandler : IEnginePhaseHandler
    {
        public EnginePhase Phase => EnginePhase.Applying;

        public Task<EnginePhase> ExecuteAsync(EngineContext context, CancellationToken ct)
        {
            context.ErrorMessage = "Simulated apply failure";
            context.ExitCode = 1;
            return Task.FromResult(EnginePhase.Failed);
        }
    }

    /// <summary>
    /// Mock registry that implements IRegistry.
    /// </summary>
    private sealed class MockRegistry : IRegistry
    {
        private readonly Dictionary<string, Dictionary<string, object?>> _keys = new(StringComparer.OrdinalIgnoreCase);

        public MockRegistry AddKey(string rootKey, string subKey)
        {
            var fullKey = $@"{rootKey}\{subKey}";
            if (!_keys.ContainsKey(fullKey))
                _keys[fullKey] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            return this;
        }

        public MockRegistry SetStringValue(string rootKey, string subKey, string valueName, string value)
        {
            var fullKey = $@"{rootKey}\{subKey}";
            if (!_keys.TryGetValue(fullKey, out var values))
            {
                values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                _keys[fullKey] = values;
            }
            values[valueName] = value;
            return this;
        }

        public bool KeyExists(string rootKey, string subKey)
        {
            return _keys.ContainsKey($@"{rootKey}\{subKey}");
        }

        public string? GetStringValue(string rootKey, string subKey, string valueName)
        {
            var fullKey = $@"{rootKey}\{subKey}";
            if (_keys.TryGetValue(fullKey, out var values) &&
                values.TryGetValue(valueName, out var value) &&
                value is string str)
                return str;
            return null;
        }

        public int? GetDWordValue(string rootKey, string subKey, string valueName)
        {
            var fullKey = $@"{rootKey}\{subKey}";
            if (_keys.TryGetValue(fullKey, out var values) &&
                values.TryGetValue(valueName, out var value) &&
                value is int dword)
                return dword;
            return null;
        }

        public IReadOnlyList<string> GetSubKeyNames(string rootKey, string subKey)
        {
            var prefix = $@"{rootKey}\{subKey}\";
            var result = new List<string>();
            foreach (var key in _keys.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var remainder = key[prefix.Length..];
                    if (!remainder.Contains('\\'))
                    {
                        result.Add(remainder);
                    }
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Mock environment that implements IEnvironment.
    /// </summary>
    private sealed class MockEnvironment : IEnvironment
    {
        private readonly Dictionary<Environment.SpecialFolder, string> _folders = new();

        public string MachineName => "TESTMACHINE";
        public bool Is64BitOperatingSystem => true;

        public MockEnvironment SetFolderPath(Environment.SpecialFolder folder, string path)
        {
            _folders[folder] = path;
            return this;
        }

        public string? GetEnvironmentVariable(string name) => null;

        public string GetFolderPath(Environment.SpecialFolder folder)
        {
            return _folders.GetValueOrDefault(folder, string.Empty);
        }
    }

    /// <summary>
    /// Mock platform services.
    /// </summary>
    private sealed class MockPlatformServices : IPlatformServices
    {
        public MockPlatformServices(IRegistry? registry = null, IEnvironment? environment = null)
        {
            FileSystem = new MockFileSystem();
            Registry = registry ?? new MockRegistry();
            Environment = environment ?? new MockEnvironment();
        }

        public IFileSystem FileSystem { get; }
        public IRegistry Registry { get; }
        public IEnvironment Environment { get; }
    }
}
