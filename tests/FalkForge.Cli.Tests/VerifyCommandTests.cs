using FalkForge.Cli.Commands;
using FalkForge.Cli.Settings;
using FalkForge.Cli.Verification;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Tests for <see cref="VerifyCommand"/> with an injected <see cref="IRebuildRunner"/> so the
/// rebuild-then-byte-compare pipeline can be exercised without invoking a real build. The fake
/// runner writes a caller-supplied artifact into the scratch output directory, standing in for
/// the project's reproducible build output.
/// </summary>
/// <remarks>
/// "SourceDateEpoch" collection: <c>Execute_ExplicitSourceDateEpochOverride_IgnoresMalformedAmbientValue</c>
/// mutates the real process SOURCE_DATE_EPOCH env var, so this class joins the same collection
/// every other SOURCE_DATE_EPOCH-mutating class in this assembly uses (see
/// <c>SourceDateEpochCollection</c>).
/// </remarks>
[Collection("SourceDateEpoch")]
public sealed class VerifyCommandTests : IDisposable
{
    private readonly List<string> _temp = [];

    private static CommandContext Ctx() =>
        new([], new EmptyRemainingArguments(), "verify", null);

    private string TempArtifact(string ext, byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"falk-verify-{Guid.NewGuid():N}{ext}");
        File.WriteAllBytes(path, bytes);
        _temp.Add(path);
        return path;
    }

    // A real on-disk project file so VerifyCommand's fail-fast existence check passes; the fake
    // runner never actually reads it.
    private string TempProject()
    {
        var path = Path.Combine(Path.GetTempPath(), $"falk-verify-proj-{Guid.NewGuid():N}.csproj");
        File.WriteAllText(path, "<Project />");
        _temp.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var p in _temp)
        {
            try { File.Delete(p); } catch { /* best effort */ }
        }
    }

    // -------------------------------------------------------------------------
    // VERIFIED: rebuild produces byte-identical artifact -> exit 0
    // -------------------------------------------------------------------------

    [Fact]
    public void Execute_IdenticalRebuild_ReturnsSuccess()
    {
        var bytes = new byte[] { 10, 20, 30, 40 };
        var artifact = TempArtifact(".msi", bytes);
        // Fake runner re-emits the SAME bytes as the rebuilt output -> VERIFIED.
        var runner = new FakeRebuildRunner(exitCode: 0, emittedArtifactBytes: bytes, emittedExt: ".msi");
        var output = new TestConsoleOutput();
        var command = new VerifyCommand(output, runner: runner);
        var settings = new VerifySettings
        {
            ArtifactPath = artifact,
            RebuildProjectPath = TempProject(),
            SourceDateEpoch = 1577836800,
        };

        var code = command.ExecuteSync(Ctx(), settings, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, code);
        Assert.Contains(output.AllOutput, m => m.Contains("VERIFIED", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Merge Gate regression (BLOCKING A): VerifyCommand's own comment says the explicit
    /// --source-date-epoch override wins and the ambient env var is never consulted in that case
    /// (see VerifyCommand.cs's "Resolve the epoch: explicit override wins..." branch). A malformed
    /// ambient SOURCE_DATE_EPOCH -- set by an unrelated tool, or left over from a previous
    /// --reproducible build in the same shell -- must not affect a verify run that supplies its
    /// own epoch explicitly.
    /// </summary>
    [Fact]
    public void Execute_ExplicitSourceDateEpochOverride_IgnoresMalformedAmbientValue()
    {
        Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", "not-a-number");
        try
        {
            var bytes = new byte[] { 10, 20, 30, 40 };
            var artifact = TempArtifact(".msi", bytes);
            var runner = new FakeRebuildRunner(exitCode: 0, emittedArtifactBytes: bytes, emittedExt: ".msi");
            var output = new TestConsoleOutput();
            var command = new VerifyCommand(output, runner: runner);
            var settings = new VerifySettings
            {
                ArtifactPath = artifact,
                RebuildProjectPath = TempProject(),
                SourceDateEpoch = 1577836800, // explicit override; ambient value above must be ignored
            };

            var code = command.ExecuteSync(Ctx(), settings, CancellationToken.None);

            Assert.Equal(ExitCodes.Success, code);
            Assert.DoesNotContain(output.AllOutput, m => m.Contains("RPR001") || m.Contains("RPR002"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", null);
        }
    }

    // -------------------------------------------------------------------------
    // MISMATCH: tampered artifact -> exit 1, diagnostic with offset
    // -------------------------------------------------------------------------

    [Fact]
    public void Execute_TamperedArtifact_ReturnsValidationFailure()
    {
        var artifact = TempArtifact(".msi", [10, 20, 30, 40]);
        // Rebuild emits different bytes at offset 2 -> MISMATCH.
        var runner = new FakeRebuildRunner(exitCode: 0, emittedArtifactBytes: [10, 20, 99, 40], emittedExt: ".msi");
        var output = new TestConsoleOutput();
        var command = new VerifyCommand(output, runner: runner);
        var settings = new VerifySettings
        {
            ArtifactPath = artifact,
            RebuildProjectPath = TempProject(),
            SourceDateEpoch = 1577836800,
        };

        var code = command.ExecuteSync(Ctx(), settings, CancellationToken.None);

        Assert.Equal(ExitCodes.ValidationFailure, code);
        Assert.Contains(output.AllOutput, m => m.Contains("MISMATCH", StringComparison.OrdinalIgnoreCase));
    }

    // -------------------------------------------------------------------------
    // REBUILD-FAILED: rebuild process non-zero -> exit 2 (compilation error)
    // -------------------------------------------------------------------------

    [Fact]
    public void Execute_RebuildProcessFails_ReturnsCompilationError()
    {
        var artifact = TempArtifact(".msi", [1, 2, 3]);
        var runner = new FakeRebuildRunner(exitCode: 1, emittedArtifactBytes: null, emittedExt: ".msi");
        var output = new TestConsoleOutput();
        var command = new VerifyCommand(output, runner: runner);
        var settings = new VerifySettings
        {
            ArtifactPath = artifact,
            RebuildProjectPath = TempProject(),
            SourceDateEpoch = 1577836800,
        };

        var code = command.ExecuteSync(Ctx(), settings, CancellationToken.None);

        Assert.Equal(ExitCodes.CompilationError, code);
        Assert.Contains(output.AllOutput, m => m.Contains("REBUILD-FAILED", StringComparison.OrdinalIgnoreCase));
    }

    // -------------------------------------------------------------------------
    // SETUP-ERROR: rebuild succeeds (exit 0) but produces no matching artifact -> exit 3.
    // The build itself succeeded, so this is NOT a REBUILD-FAILED: reusing that verdict for
    // both exit 2 (build failed) and exit 3 (no artifact) made the verdict ambiguous — the
    // same string mapped to two exit codes. No-artifact is a setup/config mismatch (the
    // project builds a different artifact type) and gets its own verdict so verdict<->exit
    // is one-to-one.
    // -------------------------------------------------------------------------

    [Fact]
    public void Execute_RebuildProducesNoArtifact_ReturnsRuntimeErrorWithSetupErrorVerdict()
    {
        var artifact = TempArtifact(".msi", [1, 2, 3]);
        var runner = new FakeRebuildRunner(exitCode: 0, emittedArtifactBytes: null, emittedExt: ".msi");
        var output = new TestConsoleOutput();
        var command = new VerifyCommand(output, runner: runner);
        var settings = new VerifySettings
        {
            ArtifactPath = artifact,
            RebuildProjectPath = TempProject(),
            SourceDateEpoch = 1577836800,
        };

        var code = command.ExecuteSync(Ctx(), settings, CancellationToken.None);

        Assert.Equal(ExitCodes.RuntimeError, code);
        // Distinct verdict — must NOT be REBUILD-FAILED (that is the exit-2 build-failure verdict).
        Assert.Contains(output.AllOutput, m => m.Contains("SETUP-ERROR", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(output.AllOutput, m => m.Contains("REBUILD-FAILED", StringComparison.OrdinalIgnoreCase));
    }

    // -------------------------------------------------------------------------
    // Missing artifact file -> exit 3
    // -------------------------------------------------------------------------

    [Fact]
    public void Execute_ArtifactFileMissing_ReturnsRuntimeError()
    {
        var runner = new FakeRebuildRunner(exitCode: 0, emittedArtifactBytes: [1], emittedExt: ".msi");
        var output = new TestConsoleOutput();
        var command = new VerifyCommand(output, runner: runner);
        var settings = new VerifySettings
        {
            ArtifactPath = "nonexistent_artifact_zzz.msi",
            RebuildProjectPath = TempProject(),
            SourceDateEpoch = 1577836800,
        };

        var code = command.ExecuteSync(Ctx(), settings, CancellationToken.None);

        Assert.Equal(ExitCodes.RuntimeError, code);
    }

    // -------------------------------------------------------------------------
    // JSON envelope: --json emits a parseable verify envelope with verdict
    // -------------------------------------------------------------------------

    [Fact]
    public void Execute_JsonMode_EmitsVerifyEnvelopeWithVerdict()
    {
        var bytes = new byte[] { 7, 7, 7 };
        var artifact = TempArtifact(".msi", bytes);
        var runner = new FakeRebuildRunner(exitCode: 0, emittedArtifactBytes: bytes, emittedExt: ".msi");
        var sink = new StringWriter();
        var command = new VerifyCommand(new TestConsoleOutput(), jsonSink: sink, runner: runner);
        var settings = new VerifySettings
        {
            ArtifactPath = artifact,
            RebuildProjectPath = TempProject(),
            SourceDateEpoch = 1577836800,
            Json = true,
        };

        var code = command.ExecuteSync(Ctx(), settings, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, code);
        var json = sink.ToString();
        Assert.Contains("\"command\":\"verify\"", json);
        Assert.Contains("verdict", json);
        Assert.Contains("VERIFIED", json);
    }
}

/// <summary>
/// Test double for <see cref="IRebuildRunner"/>. Optionally writes a caller-supplied artifact
/// into the requested output directory to simulate a project's reproducible build output.
/// </summary>
file sealed class FakeRebuildRunner : IRebuildRunner
{
    private readonly int _exitCode;
    private readonly byte[]? _emittedArtifactBytes;
    private readonly string _emittedExt;

    public FakeRebuildRunner(int exitCode, byte[]? emittedArtifactBytes, string emittedExt)
    {
        _exitCode = exitCode;
        _emittedArtifactBytes = emittedArtifactBytes;
        _emittedExt = emittedExt;
    }

    public Task<RebuildResult> RebuildAsync(
        string projectPath, string outputDir, long sourceDateEpoch,
        TimeSpan timeout, CancellationToken ct)
    {
        if (_emittedArtifactBytes is not null)
        {
            Directory.CreateDirectory(outputDir);
            File.WriteAllBytes(
                Path.Combine(outputDir, $"rebuilt{_emittedExt}"),
                _emittedArtifactBytes);
        }

        return Task.FromResult(new RebuildResult(_exitCode, "rebuild stdout", _exitCode == 0 ? "" : "build error"));
    }
}
