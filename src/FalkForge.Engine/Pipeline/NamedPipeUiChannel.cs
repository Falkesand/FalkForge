namespace FalkForge.Engine.Pipeline;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FalkForge.Diagnostics;
using FalkForge.Engine.Logging;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Transport;

/// <summary>
/// Production <see cref="IUiChannel"/> that bridges a <see cref="PipeServer"/> to the
/// <see cref="IInstallerPipeline"/> contract. Translates outbound <see cref="PipelineEvent"/>
/// values into typed <see cref="EngineMessage"/> writes, and inbound <see cref="EngineMessage"/>
/// callbacks from the pipe into <see cref="UiRequest"/> values delivered via an unbounded
/// channel. Stateful message accumulation: <c>SetInstallDirectory</c> and
/// <c>SetFeatureSelection</c> messages are buffered and bundled into the
/// <see cref="UiRequest.Plan"/> when <c>RequestPlan</c> arrives.
/// </summary>
public sealed class NamedPipeUiChannel : IUiChannel
{
    private readonly PipeServer? _pipe;
    private readonly Channel<UiRequest> _requests;

    // Mutable pre-plan state accumulated from SetInstallDirectory / SetFeatureSelection messages
    private volatile string? _pendingInstallDirectory;
    private readonly ConcurrentDictionary<string, bool> _pendingFeatures = new();

    // License state accumulated from LicenseMessage(Accepted/Declined) before RequestPlan
    private volatile bool _licenseAccepted;
    private volatile bool _licenseResponseReceived;

    // User properties accumulated from SetProperty / SetSecureProperty before RequestPlan
    private readonly ConcurrentDictionary<string, string> _pendingProperties =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SensitiveBytes> _pendingSecureProperties =
        new(StringComparer.OrdinalIgnoreCase);

    // Session correlation id stamped on outgoing Log and PhaseChanged messages.
    // Set once by EngineSession before any events are sent. Volatile write is safe
    // because only the engine thread sets it (before any SendAsync calls).
    private Guid _sessionCorrelationId;

    private NamedPipeUiChannel(PipeServer? pipe)
    {
        _pipe = pipe;
        _requests = Channel.CreateUnbounded<UiRequest>(new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = false
        });
    }

    /// <summary>
    /// Creates a channel that owns a new <see cref="PipeServer"/> for the given
    /// <paramref name="options"/>. Call <see cref="StartAsync"/> to perform the HMAC
    /// handshake before using the channel.
    /// </summary>
    public static NamedPipeUiChannel Create(PipeConnectionOptions options)
    {
        // Two-step construction: channel is created first so HandleIncomingMessageAsync
        // exists as a closure before PipeServer captures it.
        NamedPipeUiChannel? ch = null;
        var pipe = new PipeServer(options, msg => ch!.HandleIncomingMessageAsync(msg));
        ch = new NamedPipeUiChannel(pipe);
        return ch;
    }

    /// <summary>
    /// Waits for a UI client to connect and performs the HMAC handshake.
    /// Must be called once before <see cref="SendAsync"/> or
    /// <see cref="ReadRequestsAsync"/>.
    /// </summary>
    public Task<Result<Unit>> StartAsync(CancellationToken ct) =>
        _pipe!.StartAsync(ct);

    /// <summary>
    /// Creates a headless (no-pipe) channel that silently drops outbound events
    /// and immediately returns an empty request stream. Used in CLI / test scenarios
    /// where no UI process is connected.
    /// </summary>
    public static NamedPipeUiChannel CreateNullChannel()
    {
        var ch = new NamedPipeUiChannel(null);
        ch._requests.Writer.TryComplete();
        return ch;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IUiChannel
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets the session correlation id that will be stamped on outgoing
    /// <see cref="LogMessage"/> and <see cref="PhaseChangedMessage"/> frames.
    /// Called once by <see cref="FalkForge.Engine.EngineSession"/> at session start.
    /// </summary>
    public void SetSessionCorrelationId(Guid id) =>
        _sessionCorrelationId = id;

    /// <inheritdoc/>
    public async Task SendAsync(PipelineEvent evt, CancellationToken ct)
    {
        if (_pipe is null || !_pipe.IsConnected) return;

        var message = TranslateEvent(evt, _sessionCorrelationId);
        if (message is null) return;

        await _pipe.SendAsync(message, ct);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<UiRequest> ReadRequestsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var request in _requests.Reader.ReadAllAsync(ct))
        {
            yield return request;
            if (request is UiRequest.Shutdown) yield break;
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        _requests.Writer.TryComplete();
        if (_pipe is not null)
            await _pipe.DisposeAsync();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Internal helpers — internal for testability (InternalsVisibleTo Engine.Tests)
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Translates a <see cref="PipelineEvent"/> to the corresponding outbound
    /// <see cref="EngineMessage"/>. Returns null for event types with no wire mapping
    /// (e.g. <see cref="PipelineEvent.RollbackStep"/> is logged as a text message).
    /// </summary>
    /// <param name="evt">The pipeline event to translate.</param>
    /// <param name="correlationId">
    /// Session correlation id stamped on <see cref="LogMessage"/> and
    /// <see cref="PhaseChangedMessage"/> frames. Pass <see cref="Guid.Empty"/> (default)
    /// when no session id is available (e.g. in unit tests).
    /// </param>
    internal static EngineMessage? TranslateEvent(PipelineEvent evt, Guid correlationId = default) => evt switch
    {
        PipelineEvent.PhaseChanged(var phase) => new PhaseChangedMessage
        {
            Phase = phase,
            SessionCorrelationId = correlationId,
        },

        PipelineEvent.Progress(var pct, var msg) => new ProgressMessage
        {
            Progress = new InstallProgress(
                Current: pct,
                Total: 100,
                CurrentPackage: msg ?? string.Empty,
                PackagePercent: pct)
        },

        PipelineEvent.Log(var level, var text) => new LogMessage
        {
            Level = level,
            Text = text,
            SessionCorrelationId = correlationId,
        },

        PipelineEvent.Failed(var kind, var message) => new ErrorMessage { Kind = kind, Message = message },

        PipelineEvent.DetectComplete(var state, var version, var features) => new DetectCompleteMessage
        {
            State = state,
            CurrentVersion = version,
            Features = features,
        },

        PipelineEvent.PlanComplete(var totalDiskSpace, var packageIds) => new PlanCompleteMessage
        {
            TotalDiskSpaceRequired = totalDiskSpace,
            PackageIds = packageIds,
        },

        PipelineEvent.ApplyComplete(var exitCode, var errorMessage) => new ApplyCompleteMessage
        {
            ExitCode = exitCode,
            ErrorMessage = errorMessage,
        },

        PipelineEvent.RollbackStep(var step) => new LogMessage
        {
            Level = step.Succeeded ? LogLevel.Info : LogLevel.Warning,
            Text = $"Rollback [{step.OperationKind}] {step.Target}: {(step.Succeeded ? "ok" : step.Error?.Message ?? "failed")}",
            SessionCorrelationId = correlationId,
        },

        PipelineEvent.UpdateAvailable(var version, var url, var notes) =>
            new UpdateAvailableMessage
            {
                Version = version,
                DownloadUrl = url,
                ReleaseNotes = notes
            },

        PipelineEvent.UpdateDownloadProgress(var received, var total, var percent) =>
            new UpdateDownloadProgressMessage
            {
                BytesReceived = received,
                TotalBytes = total,
                PercentComplete = percent
            },

        PipelineEvent.UpdateReady(var version, var localPath) =>
            new UpdateReadyMessage
            {
                Version = version,
                LocalPath = localPath
            },

        PipelineEvent.DetectPackageComplete(var packageId, var state, var version) =>
            new DetectPackageCompleteMessage
            {
                PackageId = packageId,
                State = state,
                Version = version
            },

        PipelineEvent.DetectRelatedBundle(var bundleId, var relation, var installedVersion) =>
            new DetectRelatedBundleMessage
            {
                BundleId = bundleId,
                Relation = relation,
                InstalledVersion = installedVersion
            },

        PipelineEvent.PlanPackageBegin(var packageId, var displayName, var plannedAction) =>
            new PlanPackageBeginMessage
            {
                PackageId = packageId,
                DisplayName = displayName,
                PlannedAction = plannedAction
            },

        PipelineEvent.PlanPackageComplete(var packageId, var displayName, var plannedAction) =>
            new PlanPackageCompleteMessage
            {
                PackageId = packageId,
                DisplayName = displayName,
                PlannedAction = plannedAction
            },

        PipelineEvent.ApplyPackageBegin(var packageId, var displayName) =>
            new ApplyPackageBeginMessage
            {
                PackageId = packageId,
                DisplayName = displayName
            },

        PipelineEvent.ApplyPackageComplete(var packageId, var displayName, var succeeded) =>
            new ApplyPackageCompleteMessage
            {
                PackageId = packageId,
                DisplayName = displayName,
                Succeeded = succeeded
            },

        _ => null
    };

    /// <summary>
    /// Translates an inbound <see cref="EngineMessage"/> to a <see cref="UiRequest"/>.
    /// Returns null for messages that are not actionable requests (e.g. log messages,
    /// unknown types).
    /// <para>
    /// <paramref name="pendingInstallDirectory"/>, <paramref name="pendingFeatures"/>, and
    /// <paramref name="licenseAccepted"/> are accumulated state from prior messages and
    /// are bundled into <see cref="UiRequest.Plan"/> when <c>RequestPlan</c> arrives.
    /// </para>
    /// </summary>
    internal static UiRequest? TranslateMessage(
        EngineMessage message,
        string? pendingInstallDirectory,
        IDictionary<string, bool>? pendingFeatures,
        bool? licenseAccepted = null,
        IDictionary<string, string>? pendingProperties = null,
        IDictionary<string, SensitiveBytes>? pendingSecureProperties = null) => message switch
    {
        CancelMessage => new UiRequest.Cancel(),
        ShutdownRequestMessage => new UiRequest.Shutdown(),
        RequestDetectMessage => new UiRequest.Detect(),
        RequestApplyMessage => new UiRequest.Apply(),
        LaunchUpdateMessage => new UiRequest.LaunchUpdate(),

        RequestPlanMessage { Action: var action } =>
            new UiRequest.Plan(
                action,
                pendingInstallDirectory,
                (IReadOnlyDictionary<string, bool>?)pendingFeatures
                    ?? new Dictionary<string, bool>(),
                (IReadOnlyDictionary<string, string>?)pendingProperties
                    ?? new Dictionary<string, string>(),
                (IReadOnlyDictionary<string, SensitiveBytes>?)pendingSecureProperties
                    ?? new Dictionary<string, SensitiveBytes>(),
                licenseAccepted),

        _ => null
    };

    private Task HandleIncomingMessageAsync(EngineMessage message)
    {
        // Accumulate pre-plan configuration messages
        if (message is SetInstallDirectoryMessage dirMsg)
        {
            _pendingInstallDirectory = dirMsg.Directory;
            return Task.CompletedTask;
        }

        if (message is SetFeatureSelectionMessage featureMsg)
        {
            _pendingFeatures[featureMsg.FeatureId] = featureMsg.IsSelected;
            return Task.CompletedTask;
        }

        // License acceptance: the UI sends LicenseMessage(Accepted/Declined) before RequestPlan.
        // We record it so it can be bundled into UiRequest.Plan when RequestPlan arrives.
        if (message is LicenseMessage licenseMsg)
        {
            _licenseResponseReceived = true;
            _licenseAccepted = licenseMsg.Action == LicenseAction.Accepted;
            return Task.CompletedTask;
        }

        // SetProperty: validate name + value length and accumulate for bundling into UiRequest.Plan.
        if (message is SetPropertyMessage propMsg)
        {
            var rejection = PropertyNameValidator.Validate(propMsg.PropertyName, logger: null)
                ?? PropertyNameValidator.ValidateValueLength(propMsg.Value.Length, logger: null);
            if (rejection is null)
                _pendingProperties[propMsg.PropertyName] = propMsg.Value;
            return Task.CompletedTask;
        }

        // SetSecureProperty: validate name + value length and accumulate (value copied to
        // pending dict). A rejected secure value is disposed so its plaintext is zeroed.
        if (message is SetSecurePropertyMessage secureMsg)
        {
            var rejection = PropertyNameValidator.Validate(secureMsg.PropertyName, logger: null)
                ?? PropertyNameValidator.ValidateValueLength(secureMsg.SecureValue.Length, logger: null);
            if (rejection is null)
                _pendingSecureProperties[secureMsg.PropertyName] = secureMsg.SecureValue;
            else
                secureMsg.SecureValue.Dispose();
            return Task.CompletedTask;
        }

        bool? licenseAccepted = _licenseResponseReceived ? _licenseAccepted : null;
        var request = TranslateMessage(
            message,
            _pendingInstallDirectory,
            _pendingFeatures,
            licenseAccepted,
            _pendingProperties.IsEmpty ? null : new Dictionary<string, string>(_pendingProperties),
            _pendingSecureProperties.IsEmpty ? null : new Dictionary<string, SensitiveBytes>(_pendingSecureProperties));
        if (request is not null)
            _requests.Writer.TryWrite(request);

        return Task.CompletedTask;
    }
}
