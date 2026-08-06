namespace FalkForge.Engine.Tests.Detection;

using FalkForge.Engine.Detection;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Engine.Tests.Mocks;
using FalkForge.Testing;
using Xunit;

public sealed class SearchConditionEvaluatorTests
{
    [Fact]
    public void FileExists_ExistingFile_ReturnsTrue()
    {
        var fs = new MockFileSystemProvider()
            .WithFile(@"C:\Program Files\App\app.exe");
        var evaluator = new SearchConditionEvaluator(fs);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.FileExists,
            Path = @"C:\Program Files\App\app.exe"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void FileExists_MissingFile_ReturnsFalse()
    {
        var fs = new MockFileSystemProvider();
        var evaluator = new SearchConditionEvaluator(fs);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.FileExists,
            Path = @"C:\Program Files\App\app.exe"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void FileVersion_MatchingVersion_ReturnsTrue()
    {
        var fs = new MockFileSystemProvider()
            .WithFile(@"C:\Program Files\App\app.exe", new Version(2, 1, 0));
        var evaluator = new SearchConditionEvaluator(fs);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.FileVersion,
            Path = @"C:\Program Files\App\app.exe",
            Comparison = ">=",
            Value = "2.0.0"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void FileVersion_OlderVersion_ReturnsFalse()
    {
        var fs = new MockFileSystemProvider()
            .WithFile(@"C:\Program Files\App\app.exe", new Version(1, 0, 0));
        var evaluator = new SearchConditionEvaluator(fs);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.FileVersion,
            Path = @"C:\Program Files\App\app.exe",
            Comparison = ">=",
            Value = "2.0.0"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void FileVersion_MissingFile_ReturnsFalse()
    {
        var fs = new MockFileSystemProvider();
        var evaluator = new SearchConditionEvaluator(fs);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.FileVersion,
            Path = @"C:\Program Files\App\app.exe",
            Comparison = ">=",
            Value = "2.0.0"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void DirectoryExists_ExistingDir_ReturnsTrue()
    {
        var fs = new MockFileSystemProvider()
            .WithDirectory(@"C:\Program Files\App");
        var evaluator = new SearchConditionEvaluator(fs);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.DirectoryExists,
            Path = @"C:\Program Files\App"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void DirectoryExists_MissingDir_ReturnsFalse()
    {
        var fs = new MockFileSystemProvider();
        var evaluator = new SearchConditionEvaluator(fs);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.DirectoryExists,
            Path = @"C:\Program Files\App"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void RegistryExists_KeyExists_ReturnsTrue()
    {
        var fs = new MockFileSystemProvider();
        var registry = new MockRegistry().AddKey(RegistryRoot.LocalMachine, @"SOFTWARE\App");
        var evaluator = new SearchConditionEvaluator(fs, registry);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.RegistryValue,
            Path = @"HKLM\SOFTWARE\App",
            Comparison = "exists"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void RegistryExists_KeyMissing_ReturnsFalse()
    {
        var fs = new MockFileSystemProvider();
        var registry = new MockRegistry();
        var evaluator = new SearchConditionEvaluator(fs, registry);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.RegistryValue,
            Path = @"HKLM\SOFTWARE\App",
            Comparison = "exists"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void RegistryExists_ValueNameExists_ReturnsTrue()
    {
        var fs = new MockFileSystemProvider();
        var registry = new MockRegistry()
            .SetStringValue(RegistryRoot.LocalMachine, @"SOFTWARE\App", "Version", "2.0.0");
        var evaluator = new SearchConditionEvaluator(fs, registry);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.RegistryValue,
            Path = @"HKLM\SOFTWARE\App",
            Value = "Version",
            Comparison = "exists"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void RegistryExists_ValueNameMissing_ReturnsFalse()
    {
        var fs = new MockFileSystemProvider();
        var registry = new MockRegistry().AddKey(RegistryRoot.LocalMachine, @"SOFTWARE\App");
        var evaluator = new SearchConditionEvaluator(fs, registry);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.RegistryValue,
            Path = @"HKLM\SOFTWARE\App",
            Value = "Version",
            Comparison = "exists"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void RegistryValue_VersionCompare_GreaterOrEqual_ReturnsTrue()
    {
        var fs = new MockFileSystemProvider();
        var registry = new MockRegistry()
            .SetStringValue(RegistryRoot.LocalMachine, @"SOFTWARE\App", "Version", "3.1.0");
        var evaluator = new SearchConditionEvaluator(fs, registry);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.RegistryValue,
            Path = @"HKLM\SOFTWARE\App",
            Value = "Version",
            Comparison = ">=:2.0.0"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void RegistryValue_VersionCompare_OlderVersion_ReturnsFalse()
    {
        var fs = new MockFileSystemProvider();
        var registry = new MockRegistry()
            .SetStringValue(RegistryRoot.LocalMachine, @"SOFTWARE\App", "Version", "1.0.0");
        var evaluator = new SearchConditionEvaluator(fs, registry);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.RegistryValue,
            Path = @"HKLM\SOFTWARE\App",
            Value = "Version",
            Comparison = ">=:2.0.0"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void RegistryValue_StringEquals_ReturnsTrue()
    {
        var fs = new MockFileSystemProvider();
        var registry = new MockRegistry()
            .SetStringValue(RegistryRoot.LocalMachine, @"SOFTWARE\App", "Edition", "Enterprise");
        var evaluator = new SearchConditionEvaluator(fs, registry);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.RegistryValue,
            Path = @"HKLM\SOFTWARE\App",
            Value = "Edition",
            Comparison = "=:Enterprise"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void RegistryValue_StringEquals_Mismatch_ReturnsFalse()
    {
        var fs = new MockFileSystemProvider();
        var registry = new MockRegistry()
            .SetStringValue(RegistryRoot.LocalMachine, @"SOFTWARE\App", "Edition", "Standard");
        var evaluator = new SearchConditionEvaluator(fs, registry);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.RegistryValue,
            Path = @"HKLM\SOFTWARE\App",
            Value = "Edition",
            Comparison = "=:Enterprise"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void RegistryExists_KeyReadFails_ReturnsFailure()
    {
        // An ACL-denied/unreadable key (e.g. HKLM\SECURITY, a vendor key with tight ACLs) must
        // surface as a Result Failure, not throw and not report the key as absent. This is what
        // routes SearchConditionEvaluator through IRegistry.TryKeyExists instead of the bare
        // KeyExists, whose WindowsRegistry implementation lets the underlying SecurityException
        // escape uncaught.
        var fs = new MockFileSystemProvider();
        var registry = new MockRegistry().FailReadsUnder(@"SOFTWARE\App");
        var evaluator = new SearchConditionEvaluator(fs, registry);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.RegistryValue,
            Path = @"HKLM\SOFTWARE\App",
            Comparison = "exists"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
    }

    [Fact]
    public void RegistryExists_ValueReadFails_ReturnsFailure()
    {
        // Same as above, but for the value-name branch (routes through TryGetStringValue instead
        // of the bare GetStringValue).
        var fs = new MockFileSystemProvider();
        var registry = new MockRegistry().FailReadsUnder(@"SOFTWARE\App");
        var evaluator = new SearchConditionEvaluator(fs, registry);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.RegistryValue,
            Path = @"HKLM\SOFTWARE\App",
            Value = "Version",
            Comparison = "exists"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
    }

    [Fact]
    public void RegistryValue_ComparisonReadFails_ReturnsFailure()
    {
        // The version/string comparison branch reads via GetStringValue too (line ~106 in the
        // production file) — must fail closed the same way as the "exists" branch above.
        var fs = new MockFileSystemProvider();
        var registry = new MockRegistry().FailReadsUnder(@"SOFTWARE\App");
        var evaluator = new SearchConditionEvaluator(fs, registry);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.RegistryValue,
            Path = @"HKLM\SOFTWARE\App",
            Value = "Version",
            Comparison = ">=:2.0.0"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
    }

    [Fact]
    public void RegistryValue_InvalidPath_ReturnsFailure()
    {
        var fs = new MockFileSystemProvider();
        var registry = new MockRegistry();
        var evaluator = new SearchConditionEvaluator(fs, registry);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.RegistryValue,
            Path = "INVALID",
            Comparison = "exists"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.DetectionError, result.Error.Kind);
    }

    // --- REG_DWORD comparison (BuiltInPrerequisites.NetFx472 shape) ---
    // The real bug: EvaluateRegistryComparison used to read the actual value with a string-only accessor,
    // which reports SUCCESS with null for a REG_DWORD (the .NET Framework "Release" value is always a
    // DWORD, never a string) — indistinguishable from the value being absent. NetFx472() therefore reported
    // "not installed" on every machine, including ones that have the framework, which drove an unexpected
    // elevation + a silent install failure (HandleExitCode maps a redist's "already installed" non-zero
    // exit to ExitFailed). The fix falls back to a numeric read when the string read comes back null.

    [Fact]
    public void RegistryValue_DWordCompare_NetFx472Shape_AtThreshold_ReportsInstalled()
    {
        var fs = new MockFileSystemProvider();
        var registry = new MockRegistry()
            .SetDWordValue(RegistryRoot.LocalMachine,
                @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", "Release", 533509);
        var evaluator = new SearchConditionEvaluator(fs, registry);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.RegistryValue,
            Path = @"HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full",
            Value = "Release",
            Comparison = ">=:461808"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void RegistryValue_DWordCompare_NetFx472Shape_BelowThreshold_ReportsNotInstalled()
    {
        var fs = new MockFileSystemProvider();
        var registry = new MockRegistry()
            .SetDWordValue(RegistryRoot.LocalMachine,
                @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", "Release", 378389); // .NET 4.5
        var evaluator = new SearchConditionEvaluator(fs, registry);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.RegistryValue,
            Path = @"HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full",
            Value = "Release",
            Comparison = ">=:461808"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void RegistryValue_DWordCompare_ValueGenuinelyAbsent_ReportsNotInstalled()
    {
        var fs = new MockFileSystemProvider();
        var registry = new MockRegistry()
            .AddKey(RegistryRoot.LocalMachine, @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full");
        var evaluator = new SearchConditionEvaluator(fs, registry);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.RegistryValue,
            Path = @"HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full",
            Value = "Release",
            Comparison = ">=:461808"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void RegistryValue_DWordCompare_ReadFailure_PropagatesAsFailure_NotAbsent()
    {
        var fs = new MockFileSystemProvider();
        var registry = new MockRegistry()
            .SetDWordValue(RegistryRoot.LocalMachine,
                @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", "Release", 533509)
            .FailReadsUnder(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full");
        var evaluator = new SearchConditionEvaluator(fs, registry);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.RegistryValue,
            Path = @"HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full",
            Value = "Release",
            Comparison = ">=:461808"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
    }

    [Fact]
    public void RegistryValue_DWordCompare_EqualsOperator_ReturnsTrue()
    {
        // BuiltInPrerequisites.VCRedist14x64 shape: "Installed" = 1, also a REG_DWORD on a real machine.
        var fs = new MockFileSystemProvider();
        var registry = new MockRegistry()
            .SetDWordValue(RegistryRoot.LocalMachine,
                @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64", "Installed", 1);
        var evaluator = new SearchConditionEvaluator(fs, registry);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.RegistryValue,
            Path = @"HKLM\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64",
            Value = "Installed",
            Comparison = "=:1"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void RegistryValue_DWordCompare_NonIntegerExpectedValue_ReturnsFailure()
    {
        // Pins the string-vs-numeric decision: when the registry value is genuinely numeric (DWORD) but
        // the author-supplied comparison literal cannot be parsed as an integer, that is an authoring
        // mismatch — report it loudly as a Failure rather than silently comparing as "not equal" forever.
        var fs = new MockFileSystemProvider();
        var registry = new MockRegistry()
            .SetDWordValue(RegistryRoot.LocalMachine, @"SOFTWARE\App", "Release", 533509);
        var evaluator = new SearchConditionEvaluator(fs, registry);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.RegistryValue,
            Path = @"HKLM\SOFTWARE\App",
            Value = "Release",
            Comparison = ">=:not-a-number"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.DetectionError, result.Error.Kind);
    }

    // --- "exists" comparison against a non-string value (same type-blindness, different code path) ---

    [Fact]
    public void RegistryExists_ValueNameExists_DWordType_ReturnsTrue()
    {
        var fs = new MockFileSystemProvider();
        var registry = new MockRegistry()
            .SetDWordValue(RegistryRoot.LocalMachine, @"SOFTWARE\App", "Installed", 1);
        var evaluator = new SearchConditionEvaluator(fs, registry);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.RegistryValue,
            Path = @"HKLM\SOFTWARE\App",
            Value = "Installed",
            Comparison = "exists"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void RegistryExists_ReadFailure_PropagatesAsFailure_NotAbsent()
    {
        var fs = new MockFileSystemProvider();
        var registry = new MockRegistry()
            .AddKey(RegistryRoot.LocalMachine, @"SOFTWARE\App")
            .FailReadsUnder(@"SOFTWARE\App");
        var evaluator = new SearchConditionEvaluator(fs, registry);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.RegistryValue,
            Path = @"HKLM\SOFTWARE\App",
            Value = "Version",
            Comparison = "exists"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SharedFrameworkVersion — folder-enumeration detection for .NET shared frameworks.
    // Replaces the old (buggy) RegistryValue-based check for BuiltInPrerequisites.DotNet10DesktopAsPreUI;
    // see SearchConditionType.SharedFrameworkVersion's xmldoc and docs/release-notes/v0.5.0-beta.7.md.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SharedFrameworkVersion_DirectoryAtMinimum_ReturnsTrue()
    {
        var fs = new MockFileSystemProvider()
            .WithDirectory(@"C:\dotnet\shared\Microsoft.WindowsDesktop.App\10.0.0");
        var environment = new MockEnvironment().SetVariable("DOTNET_ROOT", @"C:\dotnet");
        var evaluator = new SearchConditionEvaluator(fs, environment: environment);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.SharedFrameworkVersion,
            Path = "Microsoft.WindowsDesktop.App",
            Value = "10.0.0"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void SharedFrameworkVersion_DirectoryAboveMinimum_ReturnsTrue()
    {
        // Real on-disk shape from the machine this fix was verified against: 10.0.10 present, 10.0.0
        // required.
        var fs = new MockFileSystemProvider()
            .WithDirectory(@"C:\dotnet\shared\Microsoft.WindowsDesktop.App\10.0.10");
        var environment = new MockEnvironment().SetVariable("DOTNET_ROOT", @"C:\dotnet");
        var evaluator = new SearchConditionEvaluator(fs, environment: environment);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.SharedFrameworkVersion,
            Path = "Microsoft.WindowsDesktop.App",
            Value = "10.0.0"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void SharedFrameworkVersion_OnlyOlderDirectoriesPresent_ReturnsFalse()
    {
        // Real on-disk shape: 9.0.17 and 8.0.29 present, but the required 10.0.0 is not.
        var fs = new MockFileSystemProvider()
            .WithDirectory(@"C:\dotnet\shared\Microsoft.WindowsDesktop.App\9.0.17")
            .WithDirectory(@"C:\dotnet\shared\Microsoft.WindowsDesktop.App\8.0.29");
        var environment = new MockEnvironment().SetVariable("DOTNET_ROOT", @"C:\dotnet");
        var evaluator = new SearchConditionEvaluator(fs, environment: environment);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.SharedFrameworkVersion,
            Path = "Microsoft.WindowsDesktop.App",
            Value = "10.0.0"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void SharedFrameworkVersion_NoSharedFxDirectory_ReturnsFalseWithoutThrowing()
    {
        var fs = new MockFileSystemProvider(); // dotnet root resolves, but shared\... does not exist
        var environment = new MockEnvironment().SetVariable("DOTNET_ROOT", @"C:\dotnet");
        var evaluator = new SearchConditionEvaluator(fs, environment: environment);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.SharedFrameworkVersion,
            Path = "Microsoft.WindowsDesktop.App",
            Value = "10.0.0"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void SharedFrameworkVersion_GetDirectoriesThrowsUnauthorizedAccess_ReturnsFalseWithoutThrowing()
    {
        // Real trap: Directory.Exists only needs traverse + read-attributes on the parent, so a
        // hardened enterprise image that denies list access on the shared-framework directory
        // still passes an existence check and then throws UnauthorizedAccessException on
        // enumeration. A provider that (mis)behaves this way must not crash this pre-UI probe --
        // the caller degrades to the same "no version directories found" outcome as the
        // already-covered NoSharedFxDirectory case, not an escaped exception.
        var fs = new MockFileSystemProvider()
            .WithThrowingDirectory(
                @"C:\dotnet\shared\Microsoft.WindowsDesktop.App",
                new UnauthorizedAccessException("Access to the path is denied."));
        var environment = new MockEnvironment().SetVariable("DOTNET_ROOT", @"C:\dotnet");
        var evaluator = new SearchConditionEvaluator(fs, environment: environment);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.SharedFrameworkVersion,
            Path = "Microsoft.WindowsDesktop.App",
            Value = "10.0.0"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void SharedFrameworkVersion_PrereleaseDirectoryOfHigherMajor_NeverSatisfies()
    {
        // Real on-disk shape: 11.0.0-preview.6.26359.118 is numerically a higher major than the
        // required 10.0.0, but a preview build is not a safe substitute for a required stable
        // runtime -- it must never count, by design (see SearchConditionType.SharedFrameworkVersion).
        var fs = new MockFileSystemProvider()
            .WithDirectory(@"C:\dotnet\shared\Microsoft.WindowsDesktop.App\11.0.0-preview.6.26359.118");
        var environment = new MockEnvironment().SetVariable("DOTNET_ROOT", @"C:\dotnet");
        var evaluator = new SearchConditionEvaluator(fs, environment: environment);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.SharedFrameworkVersion,
            Path = "Microsoft.WindowsDesktop.App",
            Value = "10.0.0"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void SharedFrameworkVersion_UnparsableDirectoryName_IsSkippedNotCrashed()
    {
        var fs = new MockFileSystemProvider()
            .WithDirectory(@"C:\dotnet\shared\Microsoft.WindowsDesktop.App\not-a-version")
            .WithDirectory(@"C:\dotnet\shared\Microsoft.WindowsDesktop.App\10.0.10");
        var environment = new MockEnvironment().SetVariable("DOTNET_ROOT", @"C:\dotnet");
        var evaluator = new SearchConditionEvaluator(fs, environment: environment);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.SharedFrameworkVersion,
            Path = "Microsoft.WindowsDesktop.App",
            Value = "10.0.0"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void SharedFrameworkVersion_InvalidMinimumVersion_ReturnsFailure()
    {
        var fs = new MockFileSystemProvider();
        var environment = new MockEnvironment().SetVariable("DOTNET_ROOT", @"C:\dotnet");
        var evaluator = new SearchConditionEvaluator(fs, environment: environment);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.SharedFrameworkVersion,
            Path = "Microsoft.WindowsDesktop.App",
            Value = "not-a-version"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.DetectionError, result.Error.Kind);
    }

    [Fact]
    public void SharedFrameworkVersion_RootResolution_PrefersDotNetRootEnvVarOverRegistry()
    {
        // Both DOTNET_ROOT and the sharedhost registry Path resolve to DIFFERENT roots; the target
        // directory only exists under the env-var root, proving the env var is tried first.
        var fs = new MockFileSystemProvider()
            .WithDirectory(@"C:\from-env\shared\Microsoft.WindowsDesktop.App\10.0.0");
        var registry = new MockRegistry()
            .SetStringValue(
                RegistryRoot.LocalMachine,
                @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedhost",
                "Path",
                @"C:\from-registry\");
        var environment = new MockEnvironment().SetVariable("DOTNET_ROOT", @"C:\from-env");
        var evaluator = new SearchConditionEvaluator(fs, registry, environment);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.SharedFrameworkVersion,
            Path = "Microsoft.WindowsDesktop.App",
            Value = "10.0.0"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void SharedFrameworkVersion_RootResolution_FallsBackToSharedHostRegistry()
    {
        // No DOTNET_ROOT set; the sharedhost registry key's Path value is the only viable root --
        // mirrors this fix's own dev machine, where that key exists even though sharedfx does not.
        var fs = new MockFileSystemProvider()
            .WithDirectory(@"C:\from-registry\shared\Microsoft.WindowsDesktop.App\10.0.0");
        var registry = new MockRegistry()
            .SetStringValue(
                RegistryRoot.LocalMachine,
                @"SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedhost",
                "Path",
                @"C:\from-registry\");
        var evaluator = new SearchConditionEvaluator(fs, registry);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.SharedFrameworkVersion,
            Path = "Microsoft.WindowsDesktop.App",
            Value = "10.0.0"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void SharedFrameworkVersion_RootResolution_FallsBackToProgramFilesDefault()
    {
        // No DOTNET_ROOT, no registry at all -- only the %ProgramFiles% default remains.
        var fs = new MockFileSystemProvider()
            .WithDirectory(@"C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App\10.0.0");
        var environment = new MockEnvironment()
            .SetFolderPath(Environment.SpecialFolder.ProgramFiles, @"C:\Program Files");
        var evaluator = new SearchConditionEvaluator(fs, environment: environment);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.SharedFrameworkVersion,
            Path = "Microsoft.WindowsDesktop.App",
            Value = "10.0.0"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void SharedFrameworkVersion_RootUnresolvable_ReturnsFalseWithoutThrowing()
    {
        // No environment, no registry, no fallback available at all -- degrades to "not installed"
        // instead of throwing.
        var fs = new MockFileSystemProvider();
        var evaluator = new SearchConditionEvaluator(fs);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.SharedFrameworkVersion,
            Path = "Microsoft.WindowsDesktop.App",
            Value = "10.0.0"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void ProductSearch_UnsupportedType_ReturnsFailure()
    {
        var fs = new MockFileSystemProvider();
        var registry = new MockRegistry();
        var evaluator = new SearchConditionEvaluator(fs, registry);

        var condition = new SearchCondition
        {
            Type = SearchConditionType.ProductSearch,
            Path = "{GUID}"
        };

        var result = evaluator.Evaluate(condition);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.DetectionError, result.Error.Kind);
    }
}
