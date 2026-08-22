using System.Security.Cryptography;
using FalkForge.Engine.Elevation.Commands;
using FalkForge.Engine.Elevation.Tests.Mocks;
using FalkForge.Engine.Protocol.Integrity;
using Xunit;

namespace FalkForge.Engine.Elevation.Tests.Commands;

public sealed class MsiInstallCommandTests : IDisposable
{
    private readonly string _tempMsiPath;
    private readonly MockMsiApi _mockMsiApi = new();
    private readonly MsiInstallCommand _command;

    public MsiInstallCommandTests()
    {
        var raw = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.msi");
        File.WriteAllBytes(raw, [0x00]);

        // Path.GetTempPath can hand back a short (8.3) component -- C:\Users\RUNNER~1\... on a CI
        // runner is the common case. The command now passes MsiInstallProductW the path resolved
        // from the open handle, which is always the long form, so the test's own copy of the path
        // is resolved the same way. Otherwise the assertions below compare two different
        // spellings of one file and fail on machines where the two spellings differ.
        _tempMsiPath = ResolveFinalPath(raw);
        _command = new MsiInstallCommand(_mockMsiApi);
    }

    private static string ResolveFinalPath(string path)
    {
        using var stream = File.OpenRead(path);
        return HashBoundFile.TryGetFinalPath(stream.SafeFileHandle) ?? path;
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
    // Prohibited character at index 0 of the value — the very first character, before any
    // other content. IndexOfAny returns 0 here, which must still count as "found" (>= 0), not
    // be missed by a narrowed ">0" check. Covers both the general ban set and the PATCH-only
    // ban set (';' stays legal for PATCH, so a leading '&' is used there instead).
    [InlineData(" PROP=\"&whoami\"")]
    [InlineData(" PATCH=\"&a.msp;b.msp\"")]
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

    [Fact]
    public void Execute_Install_HashMismatch_ReturnsSecurityErrorAndNeverInstalls()
    {
        // The declared hash does not match the file's actual bytes — the same shape an attacker
        // gets by overwriting the cached MSI after the engine hashed it but before the elevated
        // companion installs it (TOCTOU). The companion must refuse, not install anyway.
        var wrongHash = new string('0', 64);
        var payload = BuildPayload(_tempMsiPath, string.Empty, wrongHash);

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Equal(0, _mockMsiApi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_Install_TwoFieldPayload_ReturnsSecurityErrorInsteadOfThrowing()
    {
        // A payload built by a stale (pre-hash-binding) peer, or a truncated/corrupted one,
        // carries only msiPath + additionalArgs. Reading past the end of the stream must not
        // throw an unhandled EndOfStreamException — it must fail closed as a typed SecurityError.
        var payload = BuildLegacyPayload(_tempMsiPath, string.Empty);

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Equal(0, _mockMsiApi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_Install_EmptyHash_ReturnsSecurityError()
    {
        var payload = BuildPayload(_tempMsiPath, string.Empty, string.Empty);

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Equal(0, _mockMsiApi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_Install_TooShortHash_ReturnsSecurityError()
    {
        var payload = BuildPayload(_tempMsiPath, string.Empty, "AABBCC");

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Equal(0, _mockMsiApi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_Install_WhitespaceOnlyHash_ReturnsSecurityError()
    {
        var payload = BuildPayload(_tempMsiPath, string.Empty, new string(' ', 64));

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Equal(0, _mockMsiApi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_Install_HashNotValidHex_ReturnsSecurityError()
    {
        // 64 characters, but not hex digits — Convert.TryFromHexString must reject this rather
        // than throwing, and the command must turn that rejection into SecurityError.
        var badHash = new string('G', 64);
        var payload = BuildPayload(_tempMsiPath, string.Empty, badHash);

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Equal(0, _mockMsiApi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_Install_MatchingHash_CallsInstallProductWithSamePathAndCommandLine()
    {
        var payload = BuildPayload(_tempMsiPath, " INSTALLDIR=\"C:\\App\"");

        var result = _command.Execute(payload);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, _mockMsiApi.InstallProductCallCount);
        Assert.Equal(_tempMsiPath, _mockMsiApi.LastPackagePath);
        Assert.Equal(" INSTALLDIR=\"C:\\App\"", _mockMsiApi.LastCommandLine);
    }

    [Fact]
    public void Execute_Install_HoldsFileHandleOpen_DuringInstallProduct()
    {
        // Pins the TOCTOU fix itself: hashing and then closing the handle before calling
        // InstallProduct would leave the same window open in a smaller box. The file must stay
        // locked against writes and deletes for as long as the (mocked) install call runs.
        Exception? writeAttempt = null;
        Exception? deleteAttempt = null;
        _mockMsiApi.OnInstallProductCalled = () =>
        {
            writeAttempt = Record.Exception(() =>
            {
                using var write = new FileStream(_tempMsiPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            });
            deleteAttempt = Record.Exception(() => File.Delete(_tempMsiPath));
        };

        var payload = BuildPayload(_tempMsiPath, string.Empty);
        var result = _command.Execute(payload);

        Assert.True(result.IsSuccess);
        Assert.IsType<IOException>(writeAttempt);
        Assert.IsType<IOException>(deleteAttempt);
    }

    [Theory]
    // Every other malformed-hash case here is caught by "fewer than 32 bytes decoded" on its own.
    // These two are not. Convert.FromHexString fills the 32-byte destination from the first 64
    // characters and stops, so bytesWritten is 32 and only the returned OperationStatus reveals
    // the trailing junk: NeedMoreData for the odd 65th character, DestinationTooSmall for 66.
    // The hash below is the file's real digest, so dropping the status half of the guard turns
    // both of these into a successful install -- which is what makes this test able to fail.
    [InlineData("ZZ")]
    [InlineData("A")]
    public void Execute_Install_HashLongerThanSixtyFourCharacters_ReturnsSecurityError(string suffix)
    {
        var payload = BuildPayload(_tempMsiPath, string.Empty, ComputeExpectedHash(_tempMsiPath) + suffix);

        var result = _command.Execute(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.SecurityError, result.Error.Kind);
        Assert.Equal(0, _mockMsiApi.InstallProductCallCount);
    }

    [Fact]
    public void Execute_Install_PassesTheResolvedPathOfTheFileItHashed_NotTheJunctionedInput()
    {
        // The assertion that pins the fix. Whatever string the request carried, the path handed
        // to MsiInstallProductW must name the file whose bytes were hashed, with every reparse
        // point already followed.
        var root = Directory.CreateTempSubdirectory("falkforge-msi-junction-").FullName;
        try
        {
            var realDir = Directory.CreateDirectory(Path.Combine(root, "real")).FullName;
            var link = Path.Combine(root, "cache");
            if (!TestJunction.TryCreate(link, realDir))
                Assert.Skip("Could not create an NTFS directory junction in this environment.");

            byte[] publisherBytes = [0x01, 0x02, 0x03];
            var realMsi = Path.Combine(realDir, "app.msi");
            File.WriteAllBytes(realMsi, publisherBytes);

            var junctionedPath = Path.Combine(link, "app.msi");
            var payload = BuildPayload(
                junctionedPath, string.Empty, Convert.ToHexString(SHA256.HashData(publisherBytes)));

            var result = _command.Execute(payload);

            Assert.True(result.IsSuccess);
            Assert.Equal(ResolveFinalPath(realMsi), _mockMsiApi.LastPackagePath);
            Assert.NotEqual(junctionedPath, _mockMsiApi.LastPackagePath);
        }
        finally
        {
            // Remove the reparse point first. A recursive delete of a directory that still
            // contains a junction fails with UnauthorizedAccessException, which would leave the
            // temp tree behind.
            var junction = Path.Combine(root, "cache");
            if (Directory.Exists(junction))
                Directory.Delete(junction);
            TestTemp.TryDelete(root);
        }
    }

    [Fact]
    public void Execute_Install_JunctionRepointedWhileTheHandleIsHeld_StillInstallsTheHashedFile()
    {
        // The real attack, built end to end. Creating a junction needs no privilege, so an
        // ordinary same-user process can rename the cache directory, put a junction of the same
        // name in its place, and repoint that junction while the SHA-256 pass runs. The open
        // handle does not stop the repoint: it pins the file object, and deleting a junction does
        // not touch the files under its target. Before the fix, MsiInstallProductW re-resolved
        // the request's path string and got the attacker's file.
        var root = Directory.CreateTempSubdirectory("falkforge-msi-junction-").FullName;
        try
        {
            var realDir = Directory.CreateDirectory(Path.Combine(root, "real")).FullName;
            var evilDir = Directory.CreateDirectory(Path.Combine(root, "evil")).FullName;
            var link = Path.Combine(root, "cache");
            if (!TestJunction.TryCreate(link, realDir))
                Assert.Skip("Could not create an NTFS directory junction in this environment.");

            byte[] publisherBytes = [0x01, 0x02, 0x03];
            byte[] attackerBytes = [0xDE, 0xAD, 0xBE, 0xEF];
            var realMsi = Path.Combine(realDir, "app.msi");
            File.WriteAllBytes(realMsi, publisherBytes);
            File.WriteAllBytes(Path.Combine(evilDir, "app.msi"), attackerBytes);

            var junctionedPath = Path.Combine(link, "app.msi");
            byte[]? bytesTheInstallerWouldRead = null;
            bool repointSucceeded = false;
            _mockMsiApi.OnInstallProductCalled = () =>
            {
                TestJunction.Repoint(link, evilDir);
                repointSucceeded = true;
                // Read back through the exact path the command handed the MSI engine, at the
                // moment the engine would open it.
                bytesTheInstallerWouldRead = File.ReadAllBytes(_mockMsiApi.LastPackagePath!);
            };

            var payload = BuildPayload(
                junctionedPath, string.Empty, Convert.ToHexString(SHA256.HashData(publisherBytes)));

            var result = _command.Execute(payload);

            Assert.True(result.IsSuccess);
            // The attack itself has to work, or the test proves nothing about the defence.
            Assert.True(repointSucceeded);
            Assert.Equal(attackerBytes, File.ReadAllBytes(junctionedPath));
            // ...and the install still saw the publisher's bytes.
            Assert.Equal(publisherBytes, bytesTheInstallerWouldRead);
        }
        finally
        {
            // Remove the reparse point first. A recursive delete of a directory that still
            // contains a junction fails with UnauthorizedAccessException, which would leave the
            // temp tree behind.
            var junction = Path.Combine(root, "cache");
            if (Directory.Exists(junction))
                Directory.Delete(junction);
            TestTemp.TryDelete(root);
        }
    }

    private static byte[] BuildPayload(string msiPath, string additionalArgs)
        => BuildPayload(msiPath, additionalArgs, ComputeExpectedHash(msiPath));

    private static byte[] BuildPayload(string msiPath, string additionalArgs, string expectedHash)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(msiPath);
        writer.Write(additionalArgs);
        writer.Write(expectedHash);
        writer.Flush();
        return stream.ToArray();
    }

    // Pre-hash-binding wire format (msiPath + additionalArgs only) — used to prove a truncated
    // or stale-peer payload fails closed instead of throwing.
    private static byte[] BuildLegacyPayload(string msiPath, string additionalArgs)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(msiPath);
        writer.Write(additionalArgs);
        writer.Flush();
        return stream.ToArray();
    }

    private static string ComputeExpectedHash(string msiPath)
    {
        // UNC-path and file-not-found tests pass a path that doesn't exist on disk: those cases
        // are rejected before the hash is ever read (UNC check, then File-not-found), so the
        // placeholder value never has to be a real hash — it only has to be well-formed hex.
        if (!File.Exists(msiPath))
            return new string('A', 64);

        using var stream = File.OpenRead(msiPath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static uint ReadExitCode(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);
        return reader.ReadUInt32();
    }
}
