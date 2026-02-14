using FalkInstaller.Engine.Protocol.Messages;

namespace FalkInstaller.Engine.Protocol.Serialization;

public static class MessageSerializer
{
    private const ushort ProtocolVersion = 1;

    public static byte[] Serialize(EngineMessage message)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        // Write header: [Version:ushort][Type:ushort][Length:int placeholder]
        writer.Write(ProtocolVersion);
        writer.Write((ushort)message.Type);
        var lengthPosition = stream.Position;
        writer.Write(0); // placeholder for payload length

        // Write common fields
        writer.Write(message.SequenceId);

        // Write type-specific payload
        WritePayload(writer, message);

        // Go back and write actual payload length (excludes header bytes)
        var payloadLength = (int)(stream.Position - lengthPosition - sizeof(int));
        stream.Position = lengthPosition;
        writer.Write(payloadLength);
        stream.Position = stream.Length;

        return stream.ToArray();
    }

    private static void WritePayload(BinaryWriter writer, EngineMessage message)
    {
        switch (message)
        {
            case DetectBeginMessage:
                break;

            case DetectCompleteMessage m:
                writer.Write((int)m.State);
                writer.Write(m.CurrentVersion ?? string.Empty);
                writer.Write(m.Features.Length);
                foreach (var f in m.Features)
                {
                    writer.Write(f.FeatureId);
                    writer.Write(f.Title);
                    writer.Write(f.IsSelected);
                    writer.Write(f.DiskSpaceRequired);
                }
                break;

            case PlanBeginMessage m:
                writer.Write((int)m.Action);
                break;

            case PlanCompleteMessage m:
                writer.Write(m.TotalDiskSpaceRequired);
                writer.Write(m.PackageIds.Length);
                foreach (var id in m.PackageIds)
                    writer.Write(id);
                break;

            case ApplyBeginMessage m:
                writer.Write(m.TotalPackages);
                break;

            case ApplyCompleteMessage m:
                writer.Write(m.ExitCode);
                writer.Write(m.ErrorMessage ?? string.Empty);
                break;

            case ProgressMessage m:
                writer.Write(m.Progress.Current);
                writer.Write(m.Progress.Total);
                writer.Write(m.Progress.CurrentPackage);
                break;

            case ErrorMessage m:
                writer.Write(m.Message);
                writer.Write((int)m.Kind);
                break;

            case PhaseChangedMessage m:
                writer.Write((int)m.Phase);
                break;

            case CancelMessage:
                break;

            case LogMessage m:
                writer.Write(m.Text);
                writer.Write((int)m.Level);
                break;

            case ShutdownRequestMessage:
                break;

            case ShutdownResponseMessage m:
                writer.Write(m.ExitCode);
                break;

            case SetInstallDirectoryMessage m:
                writer.Write(m.Directory);
                break;

            case SetFeatureSelectionMessage m:
                writer.Write(m.FeatureId);
                writer.Write(m.IsSelected);
                break;

            case RequestDetectMessage:
                break;

            case RequestPlanMessage m:
                writer.Write((int)m.Action);
                break;

            case RequestApplyMessage:
                break;

            case ElevateExecuteMessage m:
                writer.Write(m.CommandName);
                writer.Write(m.CommandPayload.Length);
                writer.Write(m.CommandPayload);
                break;

            case ElevateResultMessage m:
                writer.Write(m.Success);
                writer.Write(m.ErrorMessage ?? string.Empty);
                var hasPayload = m.ResultPayload is not null;
                writer.Write(hasPayload);
                if (hasPayload)
                {
                    writer.Write(m.ResultPayload!.Length);
                    writer.Write(m.ResultPayload);
                }
                break;

            default:
                throw new InvalidOperationException($"Unknown message type: {message.GetType().Name}");
        }
    }
}
