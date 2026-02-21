using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests;

public class MessageRoundtripTests
{
    private static T RoundTrip<T>(T message) where T : EngineMessage
    {
        var bytes = MessageSerializer.Serialize(message);
        var result = MessageDeserializer.Deserialize(bytes);
        Assert.True(result.IsSuccess, $"Deserialization failed: {(result.IsFailure ? result.Error.Message : "")}");
        Assert.IsType<T>(result.Value);
        return (T)result.Value;
    }

    [Fact]
    public void RoundTrip_DetectBeginMessage()
    {
        var original = new DetectBeginMessage { SequenceId = 1 };
        var deserialized = RoundTrip(original);

        Assert.Equal(MessageType.DetectBegin, deserialized.Type);
        Assert.Equal(1u, deserialized.SequenceId);
    }

    [Fact]
    public void RoundTrip_DetectCompleteMessage_WithVersion()
    {
        var features = new[]
        {
            new FeatureState("core", "Core Components", "Core runtime", true, true, true, 50_000_000L),
            new FeatureState("docs", "Documentation", null, false, false, false, 10_000_000L),
            new FeatureState("samples", "Sample Projects", "Code samples", true, false, true, 25_000_000L)
        };

        var original = new DetectCompleteMessage
        {
            SequenceId = 2,
            State = InstallState.OlderVersion,
            CurrentVersion = "2.1.0",
            Features = features
        };

        var deserialized = RoundTrip(original);

        Assert.Equal(MessageType.DetectComplete, deserialized.Type);
        Assert.Equal(2u, deserialized.SequenceId);
        Assert.Equal(InstallState.OlderVersion, deserialized.State);
        Assert.Equal("2.1.0", deserialized.CurrentVersion);
        Assert.Equal(3, deserialized.Features.Length);

        Assert.Equal("core", deserialized.Features[0].FeatureId);
        Assert.Equal("Core Components", deserialized.Features[0].Title);
        Assert.Equal("Core runtime", deserialized.Features[0].Description);
        Assert.True(deserialized.Features[0].IsSelected);
        Assert.True(deserialized.Features[0].IsRequired);
        Assert.True(deserialized.Features[0].WasPreviouslyInstalled);
        Assert.Equal(50_000_000L, deserialized.Features[0].DiskSpaceRequired);

        Assert.Equal("docs", deserialized.Features[1].FeatureId);
        Assert.Null(deserialized.Features[1].Description);
        Assert.False(deserialized.Features[1].IsSelected);
        Assert.False(deserialized.Features[1].IsRequired);
        Assert.False(deserialized.Features[1].WasPreviouslyInstalled);

        Assert.Equal("samples", deserialized.Features[2].FeatureId);
        Assert.Equal("Code samples", deserialized.Features[2].Description);
        Assert.True(deserialized.Features[2].IsSelected);
        Assert.False(deserialized.Features[2].IsRequired);
        Assert.True(deserialized.Features[2].WasPreviouslyInstalled);
    }

    [Fact]
    public void RoundTrip_DetectCompleteMessage_WithNullVersion()
    {
        var original = new DetectCompleteMessage
        {
            SequenceId = 3,
            State = InstallState.NotInstalled,
            CurrentVersion = null,
            Features = []
        };

        var deserialized = RoundTrip(original);

        Assert.Equal(InstallState.NotInstalled, deserialized.State);
        Assert.Null(deserialized.CurrentVersion);
        Assert.Empty(deserialized.Features);
    }

    [Theory]
    [InlineData(InstallAction.Install)]
    [InlineData(InstallAction.Repair)]
    [InlineData(InstallAction.Uninstall)]
    [InlineData(InstallAction.Modify)]
    public void RoundTrip_PlanBeginMessage_AllActions(InstallAction action)
    {
        var original = new PlanBeginMessage { SequenceId = 4, Action = action };
        var deserialized = RoundTrip(original);

        Assert.Equal(MessageType.PlanBegin, deserialized.Type);
        Assert.Equal(action, deserialized.Action);
    }

    [Fact]
    public void RoundTrip_PlanCompleteMessage()
    {
        var original = new PlanCompleteMessage
        {
            SequenceId = 5,
            TotalDiskSpaceRequired = 1_073_741_824L,
            PackageIds = ["runtime-x64", "app-main", "app-plugins"]
        };

        var deserialized = RoundTrip(original);

        Assert.Equal(MessageType.PlanComplete, deserialized.Type);
        Assert.Equal(5u, deserialized.SequenceId);
        Assert.Equal(1_073_741_824L, deserialized.TotalDiskSpaceRequired);
        Assert.Equal(["runtime-x64", "app-main", "app-plugins"], deserialized.PackageIds);
    }

    [Fact]
    public void RoundTrip_PlanCompleteMessage_EmptyPackages()
    {
        var original = new PlanCompleteMessage
        {
            SequenceId = 6,
            TotalDiskSpaceRequired = 0,
            PackageIds = []
        };

        var deserialized = RoundTrip(original);
        Assert.Empty(deserialized.PackageIds);
    }

    [Fact]
    public void RoundTrip_ApplyBeginMessage()
    {
        var original = new ApplyBeginMessage { SequenceId = 7, TotalPackages = 15 };
        var deserialized = RoundTrip(original);

        Assert.Equal(MessageType.ApplyBegin, deserialized.Type);
        Assert.Equal(15, deserialized.TotalPackages);
    }

    [Fact]
    public void RoundTrip_ApplyCompleteMessage_WithError()
    {
        var original = new ApplyCompleteMessage
        {
            SequenceId = 8,
            ExitCode = 1603,
            ErrorMessage = "Installation failed: access denied"
        };

        var deserialized = RoundTrip(original);

        Assert.Equal(MessageType.ApplyComplete, deserialized.Type);
        Assert.Equal(1603, deserialized.ExitCode);
        Assert.Equal("Installation failed: access denied", deserialized.ErrorMessage);
    }

    [Fact]
    public void RoundTrip_ApplyCompleteMessage_Success()
    {
        var original = new ApplyCompleteMessage
        {
            SequenceId = 9,
            ExitCode = 0,
            ErrorMessage = null
        };

        var deserialized = RoundTrip(original);

        Assert.Equal(0, deserialized.ExitCode);
        Assert.Null(deserialized.ErrorMessage);
    }

    [Fact]
    public void RoundTrip_ProgressMessage()
    {
        var original = new ProgressMessage
        {
            SequenceId = 10,
            Progress = new InstallProgress(7, 25, "Microsoft.NETCore.App.Runtime")
        };

        var deserialized = RoundTrip(original);

        Assert.Equal(MessageType.Progress, deserialized.Type);
        Assert.Equal(7, deserialized.Progress.Current);
        Assert.Equal(25, deserialized.Progress.Total);
        Assert.Equal("Microsoft.NETCore.App.Runtime", deserialized.Progress.CurrentPackage);
    }

    [Theory]
    [InlineData(ErrorKind.ProtocolError)]
    [InlineData(ErrorKind.TransportError)]
    [InlineData(ErrorKind.EngineError)]
    [InlineData(ErrorKind.ElevationError)]
    public void RoundTrip_ErrorMessage_AllKinds(ErrorKind kind)
    {
        var original = new ErrorMessage
        {
            SequenceId = 11,
            Message = $"Error of kind {kind}",
            Kind = kind
        };

        var deserialized = RoundTrip(original);

        Assert.Equal(MessageType.Error, deserialized.Type);
        Assert.Equal($"Error of kind {kind}", deserialized.Message);
        Assert.Equal(kind, deserialized.Kind);
    }

    [Theory]
    [InlineData(EnginePhase.Initializing)]
    [InlineData(EnginePhase.Detecting)]
    [InlineData(EnginePhase.Planning)]
    [InlineData(EnginePhase.Elevating)]
    [InlineData(EnginePhase.Applying)]
    [InlineData(EnginePhase.Completing)]
    [InlineData(EnginePhase.Failed)]
    [InlineData(EnginePhase.RollingBack)]
    [InlineData(EnginePhase.Shutdown)]
    public void RoundTrip_PhaseChangedMessage_AllPhases(EnginePhase phase)
    {
        var original = new PhaseChangedMessage { SequenceId = 12, Phase = phase };
        var deserialized = RoundTrip(original);

        Assert.Equal(MessageType.PhaseChanged, deserialized.Type);
        Assert.Equal(phase, deserialized.Phase);
    }

    [Fact]
    public void RoundTrip_CancelMessage()
    {
        var original = new CancelMessage { SequenceId = 13 };
        var deserialized = RoundTrip(original);

        Assert.Equal(MessageType.Cancel, deserialized.Type);
        Assert.Equal(13u, deserialized.SequenceId);
    }

    [Theory]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Info)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    public void RoundTrip_LogMessage_AllLevels(LogLevel level)
    {
        var original = new LogMessage
        {
            SequenceId = 14,
            Text = $"Log at level {level}",
            Level = level
        };

        var deserialized = RoundTrip(original);

        Assert.Equal(MessageType.Log, deserialized.Type);
        Assert.Equal($"Log at level {level}", deserialized.Text);
        Assert.Equal(level, deserialized.Level);
    }

    [Fact]
    public void RoundTrip_ShutdownRequestMessage()
    {
        var original = new ShutdownRequestMessage { SequenceId = 15 };
        var deserialized = RoundTrip(original);

        Assert.Equal(MessageType.ShutdownRequest, deserialized.Type);
        Assert.Equal(15u, deserialized.SequenceId);
    }

    [Fact]
    public void RoundTrip_ShutdownResponseMessage()
    {
        var original = new ShutdownResponseMessage { SequenceId = 16, ExitCode = 0 };
        var deserialized = RoundTrip(original);

        Assert.Equal(MessageType.ShutdownResponse, deserialized.Type);
        Assert.Equal(0, deserialized.ExitCode);
    }

    [Fact]
    public void RoundTrip_SetInstallDirectoryMessage()
    {
        var original = new SetInstallDirectoryMessage
        {
            SequenceId = 17,
            Directory = @"C:\Program Files\MyApp"
        };

        var deserialized = RoundTrip(original);

        Assert.Equal(MessageType.SetInstallDirectory, deserialized.Type);
        Assert.Equal(@"C:\Program Files\MyApp", deserialized.Directory);
    }

    [Fact]
    public void RoundTrip_SetFeatureSelectionMessage()
    {
        var original = new SetFeatureSelectionMessage
        {
            SequenceId = 18,
            FeatureId = "optional-tools",
            IsSelected = true
        };

        var deserialized = RoundTrip(original);

        Assert.Equal(MessageType.SetFeatureSelection, deserialized.Type);
        Assert.Equal("optional-tools", deserialized.FeatureId);
        Assert.True(deserialized.IsSelected);
    }

    [Fact]
    public void RoundTrip_RequestDetectMessage()
    {
        var original = new RequestDetectMessage { SequenceId = 19 };
        var deserialized = RoundTrip(original);

        Assert.Equal(MessageType.RequestDetect, deserialized.Type);
        Assert.Equal(19u, deserialized.SequenceId);
    }

    [Fact]
    public void RoundTrip_RequestPlanMessage()
    {
        var original = new RequestPlanMessage
        {
            SequenceId = 20,
            Action = InstallAction.Uninstall
        };

        var deserialized = RoundTrip(original);

        Assert.Equal(MessageType.RequestPlan, deserialized.Type);
        Assert.Equal(InstallAction.Uninstall, deserialized.Action);
    }

    [Fact]
    public void RoundTrip_RequestApplyMessage()
    {
        var original = new RequestApplyMessage { SequenceId = 21 };
        var deserialized = RoundTrip(original);

        Assert.Equal(MessageType.RequestApply, deserialized.Type);
        Assert.Equal(21u, deserialized.SequenceId);
    }

    [Fact]
    public void RoundTrip_ElevateExecuteMessage()
    {
        var payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x01, 0x02 };
        var original = new ElevateExecuteMessage
        {
            SequenceId = 22,
            CommandName = "run-msi-install",
            CommandPayload = payload
        };

        var deserialized = RoundTrip(original);

        Assert.Equal(MessageType.ElevateExecute, deserialized.Type);
        Assert.Equal("run-msi-install", deserialized.CommandName);
        Assert.Equal(payload, deserialized.CommandPayload);
    }

    [Fact]
    public void RoundTrip_ElevateExecuteMessage_EmptyPayload()
    {
        var original = new ElevateExecuteMessage
        {
            SequenceId = 23,
            CommandName = "no-op",
            CommandPayload = []
        };

        var deserialized = RoundTrip(original);

        Assert.Equal("no-op", deserialized.CommandName);
        Assert.Empty(deserialized.CommandPayload);
    }

    [Fact]
    public void RoundTrip_ElevateResultMessage_Success_WithPayload()
    {
        var resultPayload = new byte[] { 0x01, 0x02, 0x03 };
        var original = new ElevateResultMessage
        {
            SequenceId = 24,
            Success = true,
            ErrorMessage = null,
            ResultPayload = resultPayload
        };

        var deserialized = RoundTrip(original);

        Assert.Equal(MessageType.ElevateResult, deserialized.Type);
        Assert.True(deserialized.Success);
        Assert.Null(deserialized.ErrorMessage);
        Assert.NotNull(deserialized.ResultPayload);
        Assert.Equal(resultPayload, deserialized.ResultPayload);
    }

    [Fact]
    public void RoundTrip_ElevateResultMessage_Failure_WithError()
    {
        var original = new ElevateResultMessage
        {
            SequenceId = 25,
            Success = false,
            ErrorMessage = "Access denied: elevation required",
            ResultPayload = null
        };

        var deserialized = RoundTrip(original);

        Assert.False(deserialized.Success);
        Assert.Equal("Access denied: elevation required", deserialized.ErrorMessage);
        Assert.Null(deserialized.ResultPayload);
    }

    [Fact]
    public void RoundTrip_SequenceId_MaxValue()
    {
        var original = new CancelMessage { SequenceId = uint.MaxValue };
        var deserialized = RoundTrip(original);

        Assert.Equal(uint.MaxValue, deserialized.SequenceId);
    }

    [Fact]
    public void RoundTrip_SequenceId_Zero()
    {
        var original = new CancelMessage { SequenceId = 0 };
        var deserialized = RoundTrip(original);

        Assert.Equal(0u, deserialized.SequenceId);
    }

    [Fact]
    public void Deserialize_TooShort_ReturnsFailure()
    {
        var data = new byte[] { 0x01, 0x00, 0x01 }; // only 3 bytes, need 8 minimum
        var result = MessageDeserializer.Deserialize(data);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ProtocolError, result.Error.Kind);
        Assert.Contains("too short", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deserialize_WrongVersion_ReturnsFailure()
    {
        // Version = 99, Type = DetectBegin (0x0101), Length = 4
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)99);
        writer.Write((ushort)MessageType.DetectBegin);
        writer.Write(4);
        writer.Write(0u); // sequenceId
        var data = stream.ToArray();

        var result = MessageDeserializer.Deserialize(data);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ProtocolError, result.Error.Kind);
        Assert.Contains("version", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deserialize_UnknownMessageType_ReturnsFailure()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)1); // valid version
        writer.Write((ushort)0xFFFF); // unknown type
        writer.Write(4);
        writer.Write(0u);
        var data = stream.ToArray();

        var result = MessageDeserializer.Deserialize(data);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ProtocolError, result.Error.Kind);
        Assert.Contains("Unknown message type", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deserialize_TruncatedPayload_ReturnsFailure()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)1);
        writer.Write((ushort)MessageType.DetectBegin);
        writer.Write(1000); // claim 1000 bytes of payload
        writer.Write(0u);   // only 4 bytes available
        var data = stream.ToArray();

        var result = MessageDeserializer.Deserialize(data);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ProtocolError, result.Error.Kind);
        Assert.Contains("truncated", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deserialize_NegativePayloadLength_ReturnsFailure()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)1);
        writer.Write((ushort)MessageType.DetectBegin);
        writer.Write(-1); // negative length
        writer.Write(0u);
        var data = stream.ToArray();

        var result = MessageDeserializer.Deserialize(data);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ProtocolError, result.Error.Kind);
        Assert.Contains("Invalid payload length", result.Error.Message);
    }

    [Fact]
    public void Deserialize_PayloadExceedsMaxSize_ReturnsFailure()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)1);
        writer.Write((ushort)MessageType.DetectBegin);
        writer.Write(2 * 1024 * 1024); // 2MB, exceeds 1MB limit
        writer.Write(0u);
        var data = stream.ToArray();

        var result = MessageDeserializer.Deserialize(data);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ProtocolError, result.Error.Kind);
        Assert.Contains("Invalid payload length", result.Error.Message);
    }

    [Fact]
    public void Deserialize_TruncatedPayloadContent_ReturnsFailure()
    {
        // Create a valid header for a LogMessage but truncate the payload so reading fails
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)1);
        writer.Write((ushort)MessageType.Log);
        // Write a length that matches what we actually write (so header check passes)
        // but the payload itself is incomplete for the message type
        var lengthPos = stream.Position;
        writer.Write(0); // placeholder
        writer.Write(0u); // sequenceId
        // LogMessage expects a string + int, but we only write a partial string prefix
        writer.Write((byte)0xFF); // invalid length-prefixed string start
        var payloadLen = (int)(stream.Position - lengthPos - 4);
        stream.Position = lengthPos;
        writer.Write(payloadLen);
        var data = stream.ToArray();

        var result = MessageDeserializer.Deserialize(data);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.ProtocolError, result.Error.Kind);
    }

    [Fact]
    public void RoundTrip_UnicodeStrings()
    {
        var original = new SetInstallDirectoryMessage
        {
            SequenceId = 30,
            Directory = "C:\\Program Files\\M\u00f6bius\\App\u00e9ndix\\\u4e2d\u6587"
        };

        var deserialized = RoundTrip(original);

        Assert.Equal(original.Directory, deserialized.Directory);
    }

    [Fact]
    public void RoundTrip_EmptyStringFields()
    {
        var original = new LogMessage
        {
            SequenceId = 31,
            Text = "",
            Level = LogLevel.Debug
        };

        var deserialized = RoundTrip(original);

        Assert.Equal("", deserialized.Text);
    }

    [Fact]
    public void RoundTrip_LargeFeatureArray()
    {
        var features = new FeatureState[100];
        for (var i = 0; i < 100; i++)
        {
            features[i] = new FeatureState($"feat-{i}", $"Feature {i}", $"Description {i}", i % 2 == 0, i % 3 == 0, i % 4 == 0, i * 1024L);
        }

        var original = new DetectCompleteMessage
        {
            SequenceId = 32,
            State = InstallState.Installed,
            CurrentVersion = "3.0.0",
            Features = features
        };

        var deserialized = RoundTrip(original);

        Assert.Equal(100, deserialized.Features.Length);
        for (var i = 0; i < 100; i++)
        {
            Assert.Equal($"feat-{i}", deserialized.Features[i].FeatureId);
            Assert.Equal($"Feature {i}", deserialized.Features[i].Title);
            Assert.Equal($"Description {i}", deserialized.Features[i].Description);
            Assert.Equal(i % 2 == 0, deserialized.Features[i].IsSelected);
            Assert.Equal(i % 3 == 0, deserialized.Features[i].IsRequired);
            Assert.Equal(i % 4 == 0, deserialized.Features[i].WasPreviouslyInstalled);
            Assert.Equal(i * 1024L, deserialized.Features[i].DiskSpaceRequired);
        }
    }
}
