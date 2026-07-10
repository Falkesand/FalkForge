using FalkForge.Engine.Elevation.Commands;
using FalkForge.Engine.Elevation.Tests.Mocks;
using Xunit;

namespace FalkForge.Engine.Elevation.Tests.Commands;

public sealed class MsiInstallCommandTests : IDisposable
{
    private readonly string _tempMsiPath;
    private readonly MockMsiApi _mockMsiApi = new();
    private readonly MsiInstallCommand _command;

    public MsiInstallCommandTests()
    {
        _tempMsiPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.msi");
        File.WriteAllBytes(_tempMsiPath, [0x00]);
        _command = new MsiInstallCommand(_mockMsiApi);
    }

    public void Dispose()
    {
        if (File.Exists(_tempMsiPath))
            File.Delete(_tempMsiPath);
    }

    [Fact]
    public void Execute_Install_CallsInstallProduct()
    {
        // Real engine wire format: MsiExecutor.ValidateAndBuildPropertyArgs emits
        // ` NAME="VALUE"` pairs — every value is wrapped in double-quotes. A regression that
        // bans the delimiter quote wholesale breaks every property-bearing non-admin install.
        var payload = BuildPayload(_tempMsiPath, " INSTALLDIR=\"C:\\App\"");

        var result = _command.Execute(payload);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, _mockMsiApi.InstallProductCallCount);
        Assert.Equal(_tempMsiPath, _mockMsiApi.LastPackagePath);
        Assert.Equal(" INSTALLDIR=\"C:\\App\"", _mockMsiApi.LastCommandLine);
    }

    [Fact]
    public void Execute_Install_AllowsMultiplePropertyPairsWithSpacesInValues()
    {
        // Values may contain spaces (e.g. install paths); whitespace between pairs separates
        // properties. Both are legitimate engine output and must be accepted.
        var args = " INSTALLDIR=\"C:\\Program Files\\My App\" LICENSEKEY=\"ABC-123\"";
        var payload = BuildPayload(_tempMsiPath, args);

        var result = _command.Execute(payload);

        Assert.True(result.IsSuccess);
        Assert.Equal(args, _mockMsiApi.LastCommandLine);
    }

    [Fact]
    public void Execute_Install_AllowsSemicolonsInPatchValue()
    {
        // The engine joins slipstream patch paths with ';' inside the PATCH value
        // (MsiExecutor.ExecuteElevatedAsync), so ';' is legitimate there — and only there.
        var args = " INSTALLDIR=\"C:\\App\" PATCH=\"C:\\p\\a.msp;C:\\p\\b.msp\"";
        var payload = BuildPayload(_tempMsiPath, args);

        var result = _command.Execute(payload);

        Assert.True(result.IsSuccess);
        Assert.Equal(args, _mockMsiApi.LastCommandLine);
    }

    [Fact]
    public void Execute_Install_SetsUIToSilent()
    {
        var payload = BuildPayload(_tempMsiPath, string.Empty);

        _command.Execute(payload);

        Assert.Equal(1, _mockMsiApi.SetInternalUICallCount);
        Assert.Equal(2, _mockMsiApi.LastUILevel); // INSTALLUILEVEL_NONE
    }

    [Fact]
    public void Execute_Install_ReturnsSuccess()
    {
        _mockMsiApi.InstallProductReturnCode = 0;
        var payload = BuildPayload(_tempMsiPath, string.Empty);

        var result = _command.Execute(payload);

        Assert.True(result.IsSuccess);
        var exitCode = ReadExitCode(result.Value);
        Assert.Equal(0u, exitCode);
    }

    [Fact]
    public void Execute_Install_RebootRequired_ReturnsSuccess()
    {
        _mockMsiApi.InstallProductReturnCode = 3010;
        var payload = BuildPayload(_tempMsiPath, string.Empty);

        var result = _command.Execute(payload);

        Assert.True(result.IsSuccess);
        var exitCode = ReadExitCode(result.Value);
        Assert.Equal(3010u, exitCode);
    }

    [Fact]
    public void Execute_Install_Failure_ReturnsError()
    {
        _mockMsiApi.InstallProductReturnCode = 1603; // Fatal error during installation
        var payload = BuildPayload(_tempMsiPath, string.Empty);

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ExecutionError, result.Error.Kind);
        Assert.Contains("1603", result.Error.Message);
    }

    [Fact]
    public void Execute_Install_EmptyAdditionalArgs_PassesNullCommandLine()
    {
        var payload = BuildPayload(_tempMsiPath, string.Empty);

        _command.Execute(payload);

        Assert.Null(_mockMsiApi.LastCommandLine);
    }

    [Theory]
    [InlineData(@"\\server\share\evil.msi")]
    [InlineData(@"\\.\pipe\evil")]
    [InlineData(@"\\?\UNC\server\share\evil.msi")]
    public void Execute_RejectsUncPaths(string path)
    {
        var payload = BuildPayload(path, string.Empty);

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("UNC", result.Error.Message);
        Assert.Equal(0, _mockMsiApi.InstallProductCallCount);
    }

    [Theory]
    [InlineData(" PROP=\"VALUE & whoami\"")]
    [InlineData(" PROP=\"VALUE | net user\"")]
    [InlineData(" PROP=\"VALUE ; extra\"")]
    [InlineData(" PROP=\"VALUE > output.txt\"")]
    [InlineData(" PROP=\"VALUE < input.txt\"")]
    [InlineData(" NOTPATCH=\"a.msp;b.msp\"")]
    public void Execute_RejectsProhibitedChars(string additionalArgs)
    {
        // The dangerous characters are checked per-VALUE (inside the quotes), mirroring the
        // engine-side MsiExecutor.ProhibitedValueChars rule — not across the whole string,
        // where the legitimate delimiter quotes live. ';' is banned in every value except PATCH.
        var payload = BuildPayload(_tempMsiPath, additionalArgs);

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("prohibited characters", result.Error.Message);
        Assert.Equal(0, _mockMsiApi.InstallProductCallCount);
    }

    [Theory]
    // A value of `a"EVIL="x` smuggled into ` PROP="<value>"`: the embedded quote closes the
    // value early and the trailing text tries to ride in as an extra property. The closing
    // quote is not followed by a pair separator, so the structure is malformed.
    [InlineData(" PROP=\"a\"EVIL=\"x\"")]
    // A smuggled trailing quote leaves the string with an unbalanced quote count.
    [InlineData(" PROP=\"a\" EVIL=\"x")]
    // Quote at the very start of a value that never closes.
    [InlineData(" PROP=\"")]
    public void Execute_RejectsDoubleQuoteInArgs(string additionalArgs)
    {
        // Intent: a forged/misused peer must not inject an EXTRA MSI property via an embedded
        // quote in a value (the original FIX 5 finding). The defense is structural — parse the
        // NAME="VALUE" pairs and reject malformed input — NOT a wholesale ban of the quote
        // character, which is the legitimate NAME="VALUE" delimiter the engine always sends.
        // A naive whole-string blocklist without '"' would ACCEPT these strings (they contain
        // no other prohibited character), so this test fails on such a revert.
        var payload = BuildPayload(_tempMsiPath, additionalArgs);

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Contains("malformed", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _mockMsiApi.InstallProductCallCount);
    }

    [Theory]
    [InlineData("PROP=VALUE")]          // unquoted value — engine never produces this
    [InlineData(" PROP=VALUE")]         // unquoted value with pair prefix
    [InlineData(" prop=\"x\"")]         // lowercase key violates ^[A-Z_][A-Z0-9_.]*$
    [InlineData(" 0PROP=\"x\"")]        // key must not start with a digit
    [InlineData(" PROP\"x\"")]          // missing '='
    [InlineData("garbage")]             // no structure at all
    public void Execute_RejectsMalformedArgs(string additionalArgs)
    {
        // Only the exact engine wire format (space-separated NAME="VALUE" pairs, key matching
        // MsiExecutor's ^[A-Z_][A-Z0-9_.]*$ rule) is accepted; anything else is a forged or
        // corrupted request and is rejected as a security failure.
        var payload = BuildPayload(_tempMsiPath, additionalArgs);

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Equal(0, _mockMsiApi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_FileNotFound_ReturnsError()
    {
        var payload = BuildPayload(@"C:\nonexistent\fake.msi", string.Empty);

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ExecutionError, result.Error.Kind);
        Assert.Contains("not found", result.Error.Message);
        Assert.Equal(0, _mockMsiApi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_Install_ExceptionInMsiApi_ReturnsError()
    {
        _mockMsiApi.ThrowOnInstall = true;
        _mockMsiApi.ThrowMessage = "Access denied by mock";
        var payload = BuildPayload(_tempMsiPath, string.Empty);

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ExecutionError, result.Error.Kind);
        Assert.Contains("Access denied by mock", result.Error.Message);
    }

    private static byte[] BuildPayload(string msiPath, string additionalArgs)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(msiPath);
        writer.Write(additionalArgs);
        writer.Flush();
        return stream.ToArray();
    }

    private static uint ReadExitCode(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);
        return reader.ReadUInt32();
    }
}
