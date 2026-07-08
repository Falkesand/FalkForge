using FalkForge.Diagnostics;
using FalkForge.Engine.Protocol.Messages;

namespace FalkForge.Engine.Protocol.Tests.Serialization;

/// <summary>
/// Central fixture providing one canonical <see cref="EngineMessage"/> instance per message
/// type with representative field values. Used by byte-parity regression tests and
/// field-reorder detection tests.
/// </summary>
internal static class MessageSamples
{
    public static IEnumerable<EngineMessage> All()
    {
        yield return new CancelMessage { SequenceId = 1 };
        yield return new DetectBeginMessage { SequenceId = 2 };
        yield return new DetectCompleteMessage
        {
            SequenceId = 3,
            State = InstallState.OlderVersion,
            CurrentVersion = "1.2.3",
            Features =
            [
                new FeatureState("core", "Core", "Core runtime", true, true, false, 50_000_000L),
                new FeatureState("docs", "Docs", null, false, false, true, 10_000_000L),
            ],
        };
        yield return new PlanBeginMessage { SequenceId = 4, Action = InstallAction.Install };
        yield return new PlanCompleteMessage
        {
            SequenceId = 5,
            TotalDiskSpaceRequired = 123_456_789L,
            PackageIds = ["pkg-a", "pkg-b", "pkg-c"],
        };
        yield return new ApplyBeginMessage { SequenceId = 6, TotalPackages = 3 };
        yield return new ApplyCompleteMessage { SequenceId = 7, ExitCode = 0, ErrorMessage = null };
        yield return new ApplyCompleteMessage { SequenceId = 8, ExitCode = 1603, ErrorMessage = "Install failed." };
        yield return new ProgressMessage
        {
            SequenceId = 9,
            Progress = new InstallProgress(42, 100, "pkg-a", 75),
        };
        yield return new ErrorMessage { SequenceId = 10, Message = "Something went wrong.", Kind = ErrorKind.EngineError };
        yield return new PhaseChangedMessage { SequenceId = 11, Phase = EnginePhase.Applying };
        yield return new LogMessage { SequenceId = 12, Text = "Installing package.", Level = LogLevel.Info };
        yield return new ShutdownRequestMessage { SequenceId = 13 };
        yield return new ShutdownResponseMessage { SequenceId = 14, ExitCode = 0 };
        yield return new SetInstallDirectoryMessage { SequenceId = 15, Directory = @"C:\Program Files\App" };
        yield return new SetFeatureSelectionMessage { SequenceId = 16, FeatureId = "docs", IsSelected = true };
        yield return new RequestDetectMessage { SequenceId = 17 };
        yield return new RequestPlanMessage { SequenceId = 18, Action = InstallAction.Uninstall };
        yield return new RequestApplyMessage { SequenceId = 19 };
        yield return new SetPropertyMessage { SequenceId = 20, PropertyName = "INSTALLDIR", Value = @"C:\App" };
        yield return new SetSecurePropertyMessage
        {
            SequenceId = 21,
            PropertyName = "DB_PASSWORD",
            SecureValue = SensitiveBytes.FromPlaintext(new byte[] { 0x73, 0x65, 0x63, 0x72, 0x65, 0x74 }),
        };
        yield return new LicenseMessage { SequenceId = 22, Action = LicenseAction.Accepted, LicenseContent = null };
        yield return new LicenseMessage { SequenceId = 23, Action = LicenseAction.Declined, LicenseContent = "MIT License..." };
        yield return new LaunchUpdateMessage { SequenceId = 24 };
        yield return new UpdateAvailableMessage
        {
            SequenceId = 25,
            Version = "2.0.0",
            ReleaseNotes = "Bug fixes.",
            DownloadUrl = "https://example.com/update.exe",
            LocalPath = null,
        };
        yield return new UpdateReadyMessage { SequenceId = 26, Version = "2.0.0", LocalPath = @"C:\Temp\update.exe" };
        yield return new UpdateDownloadProgressMessage
        {
            SequenceId = 27,
            BytesReceived = 512_000,
            TotalBytes = 1_024_000,
            PercentComplete = 50,
        };
        yield return new ElevateExecuteMessage
        {
            SequenceId = 28,
            CommandName = "MsiInstall",
            CommandPayload = new byte[] { 0x01, 0x02, 0x03 },
        };
        yield return new ElevateProgressMessage { SequenceId = 29, Percent = 33 };
        yield return new ElevateResultMessage
        {
            SequenceId = 30,
            Success = true,
            ErrorMessage = null,
            ResultPayload = null,
        };
        yield return new ElevateResultMessage
        {
            SequenceId = 31,
            Success = false,
            ErrorMessage = "Access denied.",
            ResultPayload = new byte[] { 0xFF, 0xFE },
        };
    }
}
