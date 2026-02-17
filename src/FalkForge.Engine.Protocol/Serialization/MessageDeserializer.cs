using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Serialization;

public static class MessageDeserializer
{
    private const ushort ProtocolVersion = 1;
    private const int MaxPayloadSize = 1 * 1024 * 1024; // 1MB
    private const int MaxCollectionCount = 10_000;
    private const int MinHeaderSize = 8; // version(2) + type(2) + length(4)

    public static Result<EngineMessage> Deserialize(byte[] data)
    {
        if (data.Length < MinHeaderSize)
            return Result<EngineMessage>.Failure(ErrorKind.ProtocolError, "Message too short");

        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);

        var version = reader.ReadUInt16();
        if (version != ProtocolVersion)
            return Result<EngineMessage>.Failure(ErrorKind.ProtocolError, $"Unsupported protocol version: {version}");

        var typeValue = reader.ReadUInt16();
        if (!Enum.IsDefined((MessageType)typeValue))
            return Result<EngineMessage>.Failure(ErrorKind.ProtocolError, $"Unknown message type: 0x{typeValue:X4}");
        var type = (MessageType)typeValue;

        var length = reader.ReadInt32();
        if (length < 0 || length > MaxPayloadSize)
            return Result<EngineMessage>.Failure(ErrorKind.ProtocolError, $"Invalid payload length: {length}");

        if (stream.Length - stream.Position < length)
            return Result<EngineMessage>.Failure(ErrorKind.ProtocolError, "Payload truncated");

        var sequenceId = reader.ReadUInt32();

        return ReadPayload(reader, type, sequenceId);
    }

    private static Result<EngineMessage> ReadPayload(BinaryReader reader, MessageType type, uint sequenceId)
    {
        try
        {
            return type switch
            {
                MessageType.DetectBegin => new DetectBeginMessage { SequenceId = sequenceId },
                MessageType.DetectComplete => ReadDetectComplete(reader, sequenceId),
                MessageType.PlanBegin => new PlanBeginMessage
                    { SequenceId = sequenceId, Action = (InstallAction)reader.ReadInt32() },
                MessageType.PlanComplete => ReadPlanComplete(reader, sequenceId),
                MessageType.ApplyBegin => new ApplyBeginMessage
                    { SequenceId = sequenceId, TotalPackages = reader.ReadInt32() },
                MessageType.ApplyComplete => new ApplyCompleteMessage
                    { SequenceId = sequenceId, ExitCode = reader.ReadInt32(), ErrorMessage = ReadNullableString(reader) },
                MessageType.Progress => new ProgressMessage
                {
                    SequenceId = sequenceId,
                    Progress = new InstallProgress(reader.ReadInt32(), reader.ReadInt32(), reader.ReadString())
                },
                MessageType.Error => new ErrorMessage
                    { SequenceId = sequenceId, Message = reader.ReadString(), Kind = (ErrorKind)reader.ReadInt32() },
                MessageType.PhaseChanged => new PhaseChangedMessage
                    { SequenceId = sequenceId, Phase = (EnginePhase)reader.ReadInt32() },
                MessageType.Cancel => new CancelMessage { SequenceId = sequenceId },
                MessageType.Log => new LogMessage
                    { SequenceId = sequenceId, Text = reader.ReadString(), Level = (LogLevel)reader.ReadInt32() },
                MessageType.ShutdownRequest => new ShutdownRequestMessage { SequenceId = sequenceId },
                MessageType.ShutdownResponse => new ShutdownResponseMessage
                    { SequenceId = sequenceId, ExitCode = reader.ReadInt32() },
                MessageType.SetInstallDirectory => new SetInstallDirectoryMessage
                    { SequenceId = sequenceId, Directory = reader.ReadString() },
                MessageType.SetFeatureSelection => new SetFeatureSelectionMessage
                    { SequenceId = sequenceId, FeatureId = reader.ReadString(), IsSelected = reader.ReadBoolean() },
                MessageType.RequestDetect => new RequestDetectMessage { SequenceId = sequenceId },
                MessageType.RequestPlan => new RequestPlanMessage
                    { SequenceId = sequenceId, Action = (InstallAction)reader.ReadInt32() },
                MessageType.RequestApply => new RequestApplyMessage { SequenceId = sequenceId },
                MessageType.ElevateExecute => ReadElevateExecute(reader, sequenceId),
                MessageType.ElevateResult => ReadElevateResult(reader, sequenceId),
                MessageType.UpdateAvailable => new UpdateAvailableMessage
                {
                    SequenceId = sequenceId,
                    Version = reader.ReadString(),
                    ReleaseNotes = ReadNullableString(reader),
                    DownloadUrl = reader.ReadString(),
                    LocalPath = ReadNullableString(reader)
                },
                MessageType.UpdateReady => new UpdateReadyMessage
                {
                    SequenceId = sequenceId,
                    Version = reader.ReadString(),
                    LocalPath = reader.ReadString()
                },
                _ => throw new InvalidOperationException($"Unhandled message type: {type}")
            };
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException)
        {
            return Result<EngineMessage>.Failure(ErrorKind.ProtocolError, $"Failed to read message payload: {ex.Message}");
        }
    }

    private static string? ReadNullableString(BinaryReader reader)
    {
        var s = reader.ReadString();
        return s.Length == 0 ? null : s;
    }

    private static Result<EngineMessage> ReadDetectComplete(BinaryReader reader, uint sequenceId)
    {
        var state = (InstallState)reader.ReadInt32();
        var currentVersion = ReadNullableString(reader);
        var featureCount = reader.ReadInt32();
        if (featureCount < 0 || featureCount > MaxCollectionCount)
            return Result<EngineMessage>.Failure(ErrorKind.ProtocolError, $"Collection count out of range: {featureCount}");

        var features = new FeatureState[featureCount];
        for (var i = 0; i < featureCount; i++)
        {
            var featureId = reader.ReadString();
            var title = reader.ReadString();
            var description = ReadNullableString(reader);
            var isSelected = reader.ReadBoolean();
            var isRequired = reader.ReadBoolean();
            var wasPreviouslyInstalled = reader.ReadBoolean();
            var diskSpaceRequired = reader.ReadInt64();
            features[i] = new FeatureState(
                featureId, title, description, isSelected, isRequired, wasPreviouslyInstalled, diskSpaceRequired);
        }

        return new DetectCompleteMessage
        {
            SequenceId = sequenceId,
            State = state,
            CurrentVersion = currentVersion,
            Features = features
        };
    }

    private static Result<EngineMessage> ReadPlanComplete(BinaryReader reader, uint sequenceId)
    {
        var diskSpace = reader.ReadInt64();
        var count = reader.ReadInt32();
        if (count < 0 || count > MaxCollectionCount)
            return Result<EngineMessage>.Failure(ErrorKind.ProtocolError, $"Collection count out of range: {count}");

        var packageIds = new string[count];
        for (var i = 0; i < count; i++)
            packageIds[i] = reader.ReadString();

        return new PlanCompleteMessage
        {
            SequenceId = sequenceId,
            TotalDiskSpaceRequired = diskSpace,
            PackageIds = packageIds
        };
    }

    private static Result<EngineMessage> ReadElevateExecute(BinaryReader reader, uint sequenceId)
    {
        var commandName = reader.ReadString();
        var payloadLength = reader.ReadInt32();
        if (payloadLength < 0 || payloadLength > MaxPayloadSize)
            return Result<EngineMessage>.Failure(ErrorKind.ProtocolError, $"Collection count out of range: {payloadLength}");

        var payload = reader.ReadBytes(payloadLength);

        return new ElevateExecuteMessage
        {
            SequenceId = sequenceId,
            CommandName = commandName,
            CommandPayload = payload
        };
    }

    private static Result<EngineMessage> ReadElevateResult(BinaryReader reader, uint sequenceId)
    {
        var success = reader.ReadBoolean();
        var errorMessage = ReadNullableString(reader);
        var hasPayload = reader.ReadBoolean();
        byte[]? resultPayload = null;
        if (hasPayload)
        {
            var length = reader.ReadInt32();
            if (length < 0 || length > MaxPayloadSize)
                return Result<EngineMessage>.Failure(ErrorKind.ProtocolError, $"Collection count out of range: {length}");

            resultPayload = reader.ReadBytes(length);
        }

        return new ElevateResultMessage
        {
            SequenceId = sequenceId,
            Success = success,
            ErrorMessage = errorMessage,
            ResultPayload = resultPayload
        };
    }
}
