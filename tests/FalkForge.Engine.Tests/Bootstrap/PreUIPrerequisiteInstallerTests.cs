namespace FalkForge.Engine.Tests.Bootstrap;

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FalkForge.Engine.Bootstrap;
using FalkForge.Engine.Execution;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

/// <summary>
/// TDD spec for PreUIPrerequisiteInstaller — rows 16-19 of the Phase 3 plan, plus the
/// payload hash-binding tests that close the TOCTOU gap between extraction and the
/// elevated launch (fix/elevated-payload-hash-binding).
/// </summary>
public sealed class PreUIPrerequisiteInstallerTests : IDisposable
{
    // A real temp directory per test instance (xUnit constructs a fresh instance per [Fact]),
    // because RunAllAsync now opens and hashes the payload file for real — the old "C:\extract"
    // placeholder path never had to exist on disk before this change.
    private readonly string _extractionDir = Directory.CreateTempSubdirectory("falkforge-preui-").FullName;

    private static readonly byte[] DefaultPayloadBytes = Encoding.UTF8.GetBytes("preui-test-payload");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_extractionDir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; a locked handle on a slow CI disk must not fail the test run.
        }
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>
    /// Writes <paramref name="bytes"/> (or <see cref="DefaultPayloadBytes"/>) to
    /// <c>{extractionDir}/preui/{relativeSourcePath}</c> and returns the full path, so the
    /// payload the installer opens is the exact payload the test's hash was computed from.
    /// </summary>
    private string CreatePayloadFile(string relativeSourcePath, byte[]? bytes = null)
    {
        var fullPath = Path.Combine(_extractionDir, "preui", relativeSourcePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, bytes ?? DefaultPayloadBytes);
        return fullPath;
    }

    private static PreUIPackageInfo MakePackage(
        string id = "pkg1",
        string displayName = "Test Package",
        PreUIRebootBehavior rebootBehavior = PreUIRebootBehavior.IgnoreAndContinue,
        string? sha256Hash = null)
        => new()
        {
            Id = id,
            DisplayName = displayName,
            SourcePath = $"{id}.exe",
            Sha256Hash = sha256Hash ?? Sha256Hex(DefaultPayloadBytes),
            Arguments = "/quiet /norestart",
            RebootBehavior = rebootBehavior
        };

    private PreUIPrerequisiteInstaller MakeInstaller(IProcessRunner runner)
        => new(runner, _extractionDir, logger: null);

    /// <summary>
    /// The path the installer hands the runner for <paramref name="relativeSourcePath"/>.
    /// <para>
    /// The installer now launches the path resolved from the open handle rather than the string
    /// it composed, so this has to resolve too. Directory.CreateTempSubdirectory sits under
    /// Path.GetTempPath, which can carry a short (8.3) component -- C:\Users\RUNNER~1\... on a CI
    /// runner is the common case -- and the resolved form always spells it out in full.
    /// </para>
    /// </summary>
    private string PathIn(string relativeSourcePath)
    {
        var combined = Path.Combine(_extractionDir, "preui", relativeSourcePath);
        return File.Exists(combined) ? ResolveFinalPath(combined) : combined;
    }

    private static string ResolveFinalPath(string path)
    {
        using var stream = File.OpenRead(path);
        return HashBoundFile.TryGetFinalPath(stream.SafeFileHandle) ?? path;
    }

    // ---------------------------------------------------------------------------
    // Row 16 — happy path: both packages exit 0
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RunAllAsync_RunsAllSuccessfully_WhenAllExitZero()
    {
        // Arrange
        CreatePayloadFile("pkg1.exe");
        CreatePayloadFile("pkg2.exe");
        var pkg1 = MakePackage("pkg1", "Package One");
        var pkg2 = MakePackage("pkg2", "Package Two");
        var runner = new FakeProcessRunner(new Dictionary<string, int>
        {
            [PathIn("pkg1.exe")] = 0,
            [PathIn("pkg2.exe")] = 0
        });
        var sink = new FakeProgressSink();
        var installer = MakeInstaller(runner);

        // Act
        var result = await installer.RunAllAsync([pkg1, pkg2], sink, CancellationToken.None);

        // Assert — result is success
        Assert.IsType<PreUIResult.Success>(result);

        // Both packages ran, in order
        Assert.Equal(2, runner.Invocations.Count);
        Assert.Contains(PathIn("pkg1.exe"), runner.Invocations[0].FileName);
        Assert.Contains(PathIn("pkg2.exe"), runner.Invocations[1].FileName);

        // Progress sink received at least 0 % and 100 %
        Assert.Contains(0, sink.Percents);
        Assert.Contains(100, sink.Percents);
    }

    // ---------------------------------------------------------------------------
    // Row 17 — 3010 with IgnoreAndContinue: treat as soft reboot, keep running
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RunAllAsync_ContinuesPastReboot3010_WhenBehaviorIsIgnoreAndContinue()
    {
        // Arrange
        CreatePayloadFile("pkg1.exe");
        CreatePayloadFile("pkg2.exe");
        var pkg1 = MakePackage("pkg1", "Soft Reboot Package", PreUIRebootBehavior.IgnoreAndContinue);
        var pkg2 = MakePackage("pkg2", "Follow-up Package");
        var runner = new FakeProcessRunner(new Dictionary<string, int>
        {
            [PathIn("pkg1.exe")] = 3010,
            [PathIn("pkg2.exe")] = 0
        });
        var sink = new FakeProgressSink();
        var installer = MakeInstaller(runner);

        // Act
        var result = await installer.RunAllAsync([pkg1, pkg2], sink, CancellationToken.None);

        // Assert — 3010 with IgnoreAndContinue is NOT treated as failure; both ran
        Assert.IsType<PreUIResult.Success>(result);
        Assert.Equal(2, runner.Invocations.Count);
    }

    // ---------------------------------------------------------------------------
    // Row 17b — 3010 with Block: stop immediately, return RebootRequired
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RunAllAsync_ReturnsRebootRequired_WhenBehaviorIsBlock_And3010()
    {
        // Arrange — pkg2 never runs, so it needs no payload file on disk.
        CreatePayloadFile("pkg1.exe");
        var pkg1 = MakePackage("pkg1", "Hard Reboot Package", PreUIRebootBehavior.Block);
        var pkg2 = MakePackage("pkg2", "Should Not Run");
        var runner = new FakeProcessRunner(new Dictionary<string, int>
        {
            [PathIn("pkg1.exe")] = 3010,
            [PathIn("pkg2.exe")] = 0
        });
        var sink = new FakeProgressSink();
        var installer = MakeInstaller(runner);

        // Act
        var result = await installer.RunAllAsync([pkg1, pkg2], sink, CancellationToken.None);

        // Assert — blocked; pkg2 never ran
        var reboot = Assert.IsType<PreUIResult.RebootRequired>(result);
        Assert.Equal("pkg1", reboot.Package.Id);
        Assert.Equal(3010, reboot.ExitCode);
        Assert.Single(runner.Invocations); // pkg2 NOT run
    }

    // ---------------------------------------------------------------------------
    // Row 17c — 1641 forced reboot: always stops, regardless of RebootBehavior
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RunAllAsync_ReturnsRebootRequired_OnForcedReboot1641_EvenWithIgnoreAndContinue()
    {
        // Arrange — 1641 must stop the run even though this package's behaviour says
        // "ignore and continue" (that setting only governs the soft 3010 case).
        CreatePayloadFile("pkg1.exe");
        var pkg1 = MakePackage("pkg1", "Forced Reboot Package", PreUIRebootBehavior.IgnoreAndContinue);
        var pkg2 = MakePackage("pkg2", "Should Not Run");
        var runner = new FakeProcessRunner(new Dictionary<string, int>
        {
            [PathIn("pkg1.exe")] = 1641,
            [PathIn("pkg2.exe")] = 0
        });
        var sink = new FakeProgressSink();
        var installer = MakeInstaller(runner);

        // Act
        var result = await installer.RunAllAsync([pkg1, pkg2], sink, CancellationToken.None);

        // Assert
        var reboot = Assert.IsType<PreUIResult.RebootRequired>(result);
        Assert.Equal("pkg1", reboot.Package.Id);
        Assert.Equal(1641, reboot.ExitCode);
        Assert.Single(runner.Invocations); // pkg2 NOT run
    }

    // ---------------------------------------------------------------------------
    // Row 18 — cancellation: child killed, result is Cancelled
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RunAllAsync_KillsChildAndReturnsCancelled_WhenCancellationRequested()
    {
        // Arrange — runner blocks until released; cancel mid-run
        CreatePayloadFile("pkg1.exe");
        var pkg = MakePackage("pkg1", "Long Running");
        var runner = new FakeProcessRunner(new Dictionary<string, int>
        {
            [PathIn("pkg1.exe")] = 0
        }, simulateLongRunning: true);
        var sink = new FakeProgressSink();
        var installer = MakeInstaller(runner);
        using var cts = new CancellationTokenSource();

        // Act — cancel after runner signals it has started
        var runTask = installer.RunAllAsync([pkg], sink, cts.Token);
        await runner.WaitForStartAsync();
        await cts.CancelAsync();

        var result = await runTask;

        // Assert — killed (tree) and result is Cancelled
        Assert.True(runner.KillTreeWasInvoked, "Expected Kill(entireProcessTree: true) to be called");
        Assert.IsType<PreUIResult.Cancelled>(result);
    }

    // ---------------------------------------------------------------------------
    // Row 19 — non-zero failure: stop immediately, return Failed
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RunAllAsync_ReturnsFailed_WhenChildExitsNonZero()
    {
        // Arrange — pkg2 never runs, so it needs no payload file on disk.
        CreatePayloadFile("pkg1.exe");
        var pkg1 = MakePackage("pkg1", "Failing Package");
        var pkg2 = MakePackage("pkg2", "Should Not Run");
        var runner = new FakeProcessRunner(new Dictionary<string, int>
        {
            [PathIn("pkg1.exe")] = 1603,
            [PathIn("pkg2.exe")] = 0
        });
        var sink = new FakeProgressSink();
        var installer = MakeInstaller(runner);

        // Act
        var result = await installer.RunAllAsync([pkg1, pkg2], sink, CancellationToken.None);

        // Assert — failed immediately; pkg2 never ran
        var failed = Assert.IsType<PreUIResult.Failed>(result);
        Assert.Equal("pkg1", failed.Package.Id);
        Assert.Equal(1603, failed.ExitCode);
        Assert.Single(runner.Invocations); // pkg2 NOT run
    }

    // ---------------------------------------------------------------------------
    // Security: path-traversal validation (c417601 review — Opus 4.6 critical)
    // These reject before the file is ever opened, so no payload file is needed.
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RunAllAsync_ReturnsFailed_WhenSourcePath_IsDotDotTraversal()
    {
        // Intent: a malicious manifest could set SourcePath = "..\..\Windows\System32\evil.exe".
        // The installer must reject any path that escapes the <cacheDir>/preui/ root BEFORE
        // constructing the launch path — preventing the elevated child from executing arbitrary files.
        var evil = new PreUIPackageInfo
        {
            Id = "pkg1", DisplayName = "Traversal Package",
            SourcePath = @"..\..\Windows\System32\evil.exe",
            Sha256Hash = Sha256Hex(DefaultPayloadBytes), Arguments = "/quiet"
        };
        var runner = new FakeProcessRunner(new Dictionary<string, int>());
        var sink = new FakeProgressSink();
        var installer = MakeInstaller(runner);

        var result = await installer.RunAllAsync([evil], sink, CancellationToken.None);

        // No process must be launched
        Assert.Empty(runner.Invocations);
        var failed = Assert.IsType<PreUIResult.Failed>(result);
        Assert.Equal(-1, failed.ExitCode); // sentinel: path-traversal rejection
    }

    [Fact]
    public async Task RunAllAsync_ReturnsFailed_WhenSourcePath_IsRooted()
    {
        // Intent: "C:\Windows\System32\notepad.exe" is an absolute path that bypasses
        // the preui/ containment check entirely. Must be rejected at the gate.
        var evil = new PreUIPackageInfo
        {
            Id = "pkg1", DisplayName = "Rooted Package",
            SourcePath = @"C:\Windows\System32\notepad.exe",
            Sha256Hash = Sha256Hex(DefaultPayloadBytes), Arguments = "/quiet"
        };
        var runner = new FakeProcessRunner(new Dictionary<string, int>());
        var sink = new FakeProgressSink();
        var installer = MakeInstaller(runner);

        var result = await installer.RunAllAsync([evil], sink, CancellationToken.None);

        Assert.Empty(runner.Invocations);
        var failed = Assert.IsType<PreUIResult.Failed>(result);
        Assert.Equal(-1, failed.ExitCode);
    }

    [Fact]
    public async Task RunAllAsync_ReturnsFailed_WhenSourcePath_ContainsAltStream()
    {
        // Intent: "foo.exe:hidden" uses NTFS alternate data streams to hide a payload.
        // Colon in the filename signals an alternate data stream — reject unconditionally.
        var evil = new PreUIPackageInfo
        {
            Id = "pkg1", DisplayName = "AltStream Package",
            SourcePath = "foo.exe:hidden",
            Sha256Hash = Sha256Hex(DefaultPayloadBytes), Arguments = "/quiet"
        };
        var runner = new FakeProcessRunner(new Dictionary<string, int>());
        var sink = new FakeProgressSink();
        var installer = MakeInstaller(runner);

        var result = await installer.RunAllAsync([evil], sink, CancellationToken.None);

        Assert.Empty(runner.Invocations);
        var failed = Assert.IsType<PreUIResult.Failed>(result);
        Assert.Equal(-1, failed.ExitCode);
    }

    [Fact]
    public async Task RunAllAsync_ReturnsFailed_WhenSourcePath_UsesDeviceNamespace()
    {
        // Intent: "\\?\C:\evil.exe" or "\\.\pipe\evil" routes through the Win32 device
        // namespace, bypassing many path checks. Must be rejected at the gate.
        var evil = new PreUIPackageInfo
        {
            Id = "pkg1", DisplayName = "DeviceNS Package",
            SourcePath = @"\\?\C:\evil.exe",
            Sha256Hash = Sha256Hex(DefaultPayloadBytes), Arguments = "/quiet"
        };
        var runner = new FakeProcessRunner(new Dictionary<string, int>());
        var sink = new FakeProgressSink();
        var installer = MakeInstaller(runner);

        var result = await installer.RunAllAsync([evil], sink, CancellationToken.None);

        Assert.Empty(runner.Invocations);
        var failed = Assert.IsType<PreUIResult.Failed>(result);
        Assert.Equal(-1, failed.ExitCode);
    }

    [Fact]
    public async Task RunAllAsync_Succeeds_WhenSourcePath_IsLegitimateRelative()
    {
        // Intent: confirm the gate doesn't block legitimate simple relative paths.
        // "dotnet-runtime.exe" (no directory separators, no special prefixes) is valid.
        CreatePayloadFile("dotnet-runtime.exe");
        var legit = new PreUIPackageInfo
        {
            Id = "pkg1", DisplayName = "Legit Package",
            SourcePath = "dotnet-runtime.exe",
            Sha256Hash = Sha256Hex(DefaultPayloadBytes), Arguments = "/quiet /norestart"
        };
        var runner = new FakeProcessRunner(new Dictionary<string, int>
        {
            [PathIn("dotnet-runtime.exe")] = 0
        });
        var sink = new FakeProgressSink();
        var installer = MakeInstaller(runner);

        var result = await installer.RunAllAsync([legit], sink, CancellationToken.None);

        Assert.IsType<PreUIResult.Success>(result);
        Assert.Single(runner.Invocations);
    }

    // ---------------------------------------------------------------------------
    // Payload hash binding (fix/elevated-payload-hash-binding) — closes the same-user
    // TOCTOU window between extraction and this elevated launch: containment alone only
    // proves the file sits inside the cache directory, not that it is the publisher's file.
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RunAllAsync_ReturnsFailed_WhenPayloadBytesDoNotMatchHash()
    {
        // Arrange — the file on disk is real, but its hash does not match the manifest value,
        // simulating a same-user process that swapped the payload after extraction.
        CreatePayloadFile("pkg1.exe", Encoding.UTF8.GetBytes("swapped-bytes"));
        var pkg1 = MakePackage("pkg1", "Tampered Package", sha256Hash: Sha256Hex(DefaultPayloadBytes));
        var runner = new FakeProcessRunner(new Dictionary<string, int>
        {
            [PathIn("pkg1.exe")] = 0
        });
        var sink = new FakeProgressSink();
        var installer = MakeInstaller(runner);

        // Act
        var result = await installer.RunAllAsync([pkg1], sink, CancellationToken.None);

        // Assert — rejected, and the process was never started (checked against the fake
        // runner directly, not just inferred from the return value).
        Assert.Empty(runner.Invocations);
        Assert.IsType<PreUIResult.Failed>(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-hex-and-too-short")]
    [InlineData("AA")] // valid hex, but only 1 byte — not a 32-byte SHA-256 digest
    public async Task RunAllAsync_ReturnsFailed_WhenHashIsMalformedOrEmpty(string malformedHash)
    {
        // Arrange
        CreatePayloadFile("pkg1.exe");
        var pkg1 = MakePackage("pkg1", "Malformed Hash Package", sha256Hash: malformedHash);
        var runner = new FakeProcessRunner(new Dictionary<string, int>
        {
            [PathIn("pkg1.exe")] = 0
        });
        var sink = new FakeProgressSink();
        var installer = MakeInstaller(runner);

        // Act
        var result = await installer.RunAllAsync([pkg1], sink, CancellationToken.None);

        // Assert — a malformed/empty hash must fail closed, never "skip the check".
        Assert.Empty(runner.Invocations);
        Assert.IsType<PreUIResult.Failed>(result);
    }

    [Fact]
    public async Task RunAllAsync_Launches_WhenPayloadBytesMatchHash()
    {
        // Arrange — matching payload must still launch and still map exit codes as before.
        CreatePayloadFile("pkg1.exe");
        var pkg1 = MakePackage("pkg1", "Verified Package");
        var runner = new FakeProcessRunner(new Dictionary<string, int>
        {
            [PathIn("pkg1.exe")] = 0
        });
        var sink = new FakeProgressSink();
        var installer = MakeInstaller(runner);

        // Act
        var result = await installer.RunAllAsync([pkg1], sink, CancellationToken.None);

        // Assert
        Assert.IsType<PreUIResult.Success>(result);
        Assert.Single(runner.Invocations);
        Assert.Contains(PathIn("pkg1.exe"), runner.Invocations[0].FileName);
    }

    [Theory]
    // Every other malformed-hash case is caught by "fewer than 32 bytes decoded" on its own.
    // These two are not. Convert.FromHexString fills the 32-byte destination from the first 64
    // characters and stops, so bytesWritten is 32 and only the returned OperationStatus reveals
    // the trailing junk: NeedMoreData for the odd 65th character, DestinationTooSmall for 66.
    // The hash below is the payload's real digest, so dropping the status half of the guard turns
    // both of these into a successful launch -- which is what makes this test able to fail.
    [InlineData("ZZ")]
    [InlineData("A")]
    public async Task RunAllAsync_ReturnsFailed_WhenHashIsLongerThanSixtyFourCharacters(string suffix)
    {
        CreatePayloadFile("pkg1.exe");
        var pkg1 = MakePackage("pkg1", "Overlong Hash Package", sha256Hash: Sha256Hex(DefaultPayloadBytes) + suffix);
        var runner = new FakeProcessRunner(new Dictionary<string, int>
        {
            [PathIn("pkg1.exe")] = 0
        });
        var sink = new FakeProgressSink();
        var installer = MakeInstaller(runner);

        var result = await installer.RunAllAsync([pkg1], sink, CancellationToken.None);

        Assert.Empty(runner.Invocations);
        Assert.IsType<PreUIResult.Failed>(result);
    }

    [Fact]
    public async Task RunAllAsync_JunctionRepointedWhileTheHandleIsHeld_StillLaunchesTheHashedFile()
    {
        // The real attack, built end to end. Creating a junction needs no privilege, so an
        // ordinary same-user process can rename the preui directory, put a junction of the same
        // name in its place, and repoint that junction while the SHA-256 pass runs. The open
        // handle does not stop the repoint: it pins the file object, and deleting a junction does
        // not touch the files under its target. Before the fix, ProcessStartInfo.FileName carried
        // the composed path string and Windows resolved it a second time, through the attacker's
        // junction.
        var realDir = Directory.CreateDirectory(Path.Combine(_extractionDir, "preui-real")).FullName;
        var evilDir = Directory.CreateDirectory(Path.Combine(_extractionDir, "preui-evil")).FullName;
        var link = Path.Combine(_extractionDir, "preui");
        if (!TestJunction.TryCreate(link, realDir))
            Assert.Skip("Could not create an NTFS directory junction in this environment.");

        var attackerBytes = Encoding.UTF8.GetBytes("attacker-payload");
        var realPayload = Path.Combine(realDir, "pkg1.exe");
        File.WriteAllBytes(realPayload, DefaultPayloadBytes);
        File.WriteAllBytes(Path.Combine(evilDir, "pkg1.exe"), attackerBytes);

        try
        {
            await AssertJunctionRepointDoesNotChangeWhatLaunchesAsync(link, evilDir, realPayload, attackerBytes);
        }
        finally
        {
            // Remove the reparse point before teardown. A recursive delete of a directory that
            // still contains a junction fails with UnauthorizedAccessException, which would mask
            // the assertions above behind a teardown crash.
            if (Directory.Exists(link))
                Directory.Delete(link);
        }
    }

    private async Task AssertJunctionRepointDoesNotChangeWhatLaunchesAsync(
        string link, string evilDir, string realPayload, byte[] attackerBytes)
    {
        var junctionedPath = Path.Combine(link, "pkg1.exe");
        var resolvedPayload = ResolveFinalPath(realPayload);

        // Both spellings map to exit code 0, so a regression fails on the byte and path
        // assertions below rather than on the fake runner not recognising the file name.
        var runner = new FakeProcessRunner(new Dictionary<string, int>
        {
            [resolvedPayload] = 0,
            [junctionedPath] = 0
        });

        byte[]? launchedBytes = null;
        var repointSucceeded = false;
        runner.OnRun = fileName =>
        {
            TestJunction.Repoint(link, evilDir);
            repointSucceeded = true;
            // Read back through the exact path handed to the runner, at the moment the child
            // process image would be mapped from it.
            launchedBytes = File.ReadAllBytes(fileName);
        };

        var sink = new FakeProgressSink();
        var installer = MakeInstaller(runner);

        var result = await installer.RunAllAsync([MakePackage("pkg1", "Junctioned Package")], sink, CancellationToken.None);

        Assert.IsType<PreUIResult.Success>(result);
        // The attack itself has to work, or the test proves nothing about the defence.
        Assert.True(repointSucceeded);
        Assert.Equal(attackerBytes, File.ReadAllBytes(junctionedPath));
        // ...and the launch still saw the publisher's bytes, at the resolved path.
        Assert.Equal(DefaultPayloadBytes, launchedBytes);
        Assert.Equal(resolvedPayload, runner.Invocations[0].FileName);
        Assert.NotEqual(junctionedPath, runner.Invocations[0].FileName);
    }

    [Fact]
    public async Task RunAllAsync_HoldsPayloadHandleOpen_ForWriteAndDeleteDenial_WhileProcessRuns()
    {
        // Arrange — this is the test that pins the handle-holding behaviour itself: while the
        // fake runner's callback is executing (standing in for "the child process is running"),
        // the payload file must be provably locked against write and delete. Hashing and then
        // closing the handle before launch would pass every other test here while still leaving
        // the TOCTOU window open, so this assertion has to run from inside the callback, at the
        // moment the handle is supposed to be held — not before or after.
        var payloadPath = CreatePayloadFile("pkg1.exe");
        var pkg1 = MakePackage("pkg1", "Locked Package");
        var runner = new FakeProcessRunner(new Dictionary<string, int>
        {
            [PathIn("pkg1.exe")] = 0
        });
        var sink = new FakeProgressSink();
        var installer = MakeInstaller(runner);

        runner.OnRun = _ =>
        {
            var writeAttempt = Record.Exception(() =>
            {
                using var writeStream = new FileStream(payloadPath, FileMode.Open, FileAccess.Write, FileShare.None);
            });
            Assert.NotNull(writeAttempt);
            Assert.IsType<IOException>(writeAttempt);

            var deleteAttempt = Record.Exception(() => File.Delete(payloadPath));
            Assert.NotNull(deleteAttempt);
            Assert.IsType<IOException>(deleteAttempt);
        };

        // Act
        var result = await installer.RunAllAsync([pkg1], sink, CancellationToken.None);

        // Assert — the run still completed successfully; the lock did not break the launch.
        Assert.IsType<PreUIResult.Success>(result);
    }
}

// =============================================================================
// Test doubles
// =============================================================================

/// <summary>
/// Controllable fake for IProcessRunner.
/// Supports per-file exit-code mapping, long-running simulation, and kill tracking.
/// </summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly IReadOnlyDictionary<string, int> _exitCodes;
    private readonly bool _simulateLongRunning;
    private readonly TaskCompletionSource _startedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _releaseTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<(string FileName, string Arguments)> Invocations { get; } = [];
    public bool KillTreeWasInvoked { get; private set; }

    /// <summary>
    /// Invoked synchronously from inside <see cref="RunAsync(string,string,CancellationToken)"/>,
    /// before the exit code is returned — lets a test observe state (e.g. file locking) while
    /// the "child process" is standing in as running.
    /// </summary>
    public Action<string>? OnRun { get; set; }

    public FakeProcessRunner(
        IReadOnlyDictionary<string, int> exitCodes,
        bool simulateLongRunning = false)
    {
        _exitCodes = exitCodes;
        _simulateLongRunning = simulateLongRunning;
    }

    /// <summary>Awaited by the test to know the fake runner has started executing.</summary>
    public Task WaitForStartAsync() => _startedTcs.Task;

    /// <summary>Unblocks the long-running simulation (for use after kill is verified).</summary>
    public void Release() => _releaseTcs.TrySetResult();

    public async Task<int> RunAsync(string fileName, string arguments, CancellationToken ct)
    {
        Invocations.Add((fileName, arguments));
        OnRun?.Invoke(fileName);

        if (_simulateLongRunning)
        {
            _startedTcs.TrySetResult();
            // Task.Delay throws OperationCanceledException on cancellation — propagated naturally.
            await Task.Delay(Timeout.Infinite, ct);
        }

        if (!_exitCodes.TryGetValue(fileName, out var code))
            throw new InvalidOperationException($"FakeProcessRunner: no exit code configured for '{fileName}'");

        return code;
    }

    public Task<int> RunAsync(string fileName, string arguments, Action<int>? onProcessStarted, CancellationToken ct)
    {
        // Fake PID is 1. The installer calls Kill separately via the KillTree method.
        onProcessStarted?.Invoke(1);
        return RunAsync(fileName, arguments, ct);
    }

    /// <summary>
    /// Called by PreUIPrerequisiteInstaller when it needs to kill the process tree.
    /// The installer must call this method (not Process.GetProcessById) so that
    /// tests can assert kill behaviour without real OS processes.
    /// </summary>
    public void KillTree(int pid) => KillTreeWasInvoked = true;
}

/// <summary>Simple progress sink that records all reported values.</summary>
internal sealed class FakeProgressSink : IProgressSink
{
    public List<string> Messages { get; } = [];
    public List<int> Percents { get; } = [];

    public void SetMessage(string text) => Messages.Add(text);
    public void SetPercent(int percent) => Percents.Add(percent);
}
