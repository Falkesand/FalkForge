# RFC: Deepen protocol serialization into per-message codec records

**Status:** Design accepted, implementation plan pending
**Author:** architectural review, 2026-04-11
**Scope:** `src/FalkForge.Engine.Protocol/Serialization/`, `src/FalkForge.Engine.Protocol/Messages/`, `src/FalkForge.Ui.Abstractions/SensitiveBytes.cs`, `tests/FalkForge.Engine.Protocol.Tests/`

## Problem

`src/FalkForge.Engine.Protocol/Serialization/MessageSerializer.cs` (191 LOC) and `MessageDeserializer.cs` (238 LOC) are paired switch statements dispatching on 28 concrete message types. Each case writes or reads the fields of its message by hand through `BinaryWriter` or `BinaryReader`, using hand-maintained field ordering that must match exactly between the two files.

```csharp
case DetectCompleteMessage m:
    writer.Write((int)m.State);
    writer.Write(m.CurrentVersion ?? string.Empty);
    writer.Write(m.Features.Length);
    foreach (var f in m.Features)
    {
        writer.Write(f.FeatureId);
        writer.Write(f.Title);
        writer.Write(f.Description ?? string.Empty);
        writer.Write(f.IsSelected);
        writer.Write(f.IsRequired);
        writer.Write(f.WasPreviouslyInstalled);
        writer.Write(f.DiskSpaceRequired);
    }
    break;
```

Adding a new message type requires editing five places: adding a value to the `MessageType` enum, creating the message record in `Messages/`, adding a serializer case with hand-written field writes, adding a matching deserializer case with hand-written field reads in exactly the same order, and writing a round-trip test. Steps three and four have no compiler help. A field added to the serializer but forgotten in the deserializer produces a runtime deserialization error only after the message is sent across the pipe. A field added in the wrong order between steps three and four produces silent corruption that the existing round-trip tests cannot detect when the mismatched fields share the same wire type.

The existing test coverage is better than expected — `MessageSerializerTests.cs` and `MessageDeserializerTests.cs` cover round-trip for every message type, header correctness, payload truncation, unknown type rejection, and version mismatch rejection. What they do not cover: field reorder detection within same-type fields, forward-compat handling of additional trailing fields from a newer wire version, and the lifecycle of secure property bytes.

**The `SetSecurePropertyMessage` lifecycle is a real security bug.** The message is advertised in `Ui.Abstractions` as the transport for DPAPI-protected passwords crossing the UI-to-engine pipe boundary. The recon into the current implementation found:

- **Write side**: `writer.Write(m.SecureValue.Length); writer.Write(m.SecureValue);` — plain length-prefixed bytes, no zeroing, no DPAPI wrap, no intermediate buffer management. The `byte[]` comes in from the caller, gets written straight into the `BinaryWriter` stream, and lingers in the caller's managed heap until GC.
- **Read side**: `var secureValue = reader.ReadBytes(payloadLength);` — allocates a plain `byte[]`, returns it as the deserialized message's `SecureValue` field. No `SensitiveBytes` wrapper, no DPAPI decryption, no zeroing lifecycle. The bytes live as plaintext in managed memory until the GC promotes them, at which point any heap dump captures the plaintext.
- **Message type**: `SetSecurePropertyMessage.SecureValue` is typed as `byte[]`. There is no way for the type system to enforce that a caller reading a deserialized message will treat the bytes as sensitive.

The contract between `Ui.Abstractions.SensitiveBytes` and DPAPI protection stops at the pipe boundary. Plaintext passwords cross the wire in a plain managed array with no zeroing discipline. This is the single most important fix in this cycle.

There is no wire schema document. To understand the byte layout of any message, a reader must open `MessageSerializer.cs` and read the relevant switch case. The field names, types, and nullability are implicit in the order of `writer.Write(...)` calls. For a cross-process binary protocol that already ships in production installers, this is a diagnostic dead-end when anything goes wrong on the wire.

Version handling is a hard gate with no downgrade path. `ProtocolVersion = 1` is hardcoded in both the serializer and the deserializer. Any incoming frame whose version header is not exactly 1 is rejected with `Result.Failure(ErrorKind.ProtocolError, "Unsupported protocol version: ...")`. If a future engine ships with a protocol v2 that adds an optional trailing field to one message, a v1 UI cannot talk to it at all — the version check rejects before the type check even runs. There is no "read v1 messages as v2, ignore unknown trailing fields" path.

This is a shallow-module problem concentrated in two paired files, compounded by a real security bug in secure-property transport and a missing version-evolution mechanism. Deepening means collapsing the paired switches into per-message codec records (one `MessageCodec<T>` field per message) held in a `FrozenDictionary` registry keyed by `(MessageType, WireVersion)`, fixing the secure property lifecycle by changing `SetSecurePropertyMessage.SecureValue` to `SensitiveBytes` and making the message `IDisposable`, and adding byte-parity regression tests against the legacy serializer kept alive during cutover.

## Proposed Interface

The design preserves the public call signatures of `MessageSerializer.Serialize` and `MessageDeserializer.Deserialize` so transport-layer callers need zero changes. Internally, both facades delegate to a per-message codec registry holding one `MessageCodec<T>` record per message type per wire version.

### Public facade — call site preserved

```csharp
namespace FalkForge.Engine.Protocol.Serialization;

/// <summary>
/// Serializes an EngineMessage into a byte array framed with the wire header.
/// Call signature preserved from the legacy switch-based implementation.
/// Internally delegates to MessageCodecRegistry.ForWrite(message).
/// </summary>
public static class MessageSerializer
{
    public const ushort CurrentWireVersion = 1;
    public static byte[] Serialize(EngineMessage message);
}

/// <summary>
/// Deserializes a framed payload (already unwrapped from transport length prefix)
/// into a typed EngineMessage. Call signature preserved. Internally delegates to
/// MessageCodecRegistry.ForRead(type, version).
/// </summary>
public static class MessageDeserializer
{
    public static Result<EngineMessage> Deserialize(ReadOnlySpan<byte> bytes);
}
```

### Codec record — rules-as-data per message

```csharp
namespace FalkForge.Engine.Protocol.Serialization;

/// <summary>
/// Immutable description of one message's wire format. One static readonly field
/// per message type per wire version. Carries write/read delegates plus field
/// schema metadata for introspection and reorder detection tests.
///
/// Direct analog of Cycle 2's ITableProducer and Cycle 4's TableReadSchema:
/// per-unit records holding metadata plus pure write/read delegates replace
/// the central switch-based dispatcher.
/// </summary>
public sealed record MessageCodec<T> : IMessageCodec where T : EngineMessage
{
    public required MessageType Type { get; init; }
    public required ushort WireVersion { get; init; }
    public required ImmutableArray<FieldDescriptor> Fields { get; init; }

    /// <summary>
    /// Pure write delegate. Static lambda — no closures, zero allocation
    /// on the hot path beyond the outer record creation.
    /// </summary>
    public required Action<BinaryWriter, T> Write { get; init; }

    /// <summary>
    /// Pure read delegate. Returns a fresh message instance.
    /// </summary>
    public required Func<BinaryReader, T> Read { get; init; }

    /// <summary>
    /// Optional post-write hook. Used by SetSecurePropertyCodec to dispose
    /// the message's SensitiveBytes after the bytes have been written to
    /// the wire, ensuring plaintext does not linger in the caller's heap.
    /// Default null (no-op).
    /// </summary>
    public Action<T>? PostWrite { get; init; }

    /// <summary>
    /// Optional post-read hook. Used by SetSecurePropertyCodec to wrap the
    /// just-read plaintext buffer in a SensitiveBytes instance before the
    /// message is returned to the caller. Default null (no-op).
    /// </summary>
    public Func<T, T>? PostRead { get; init; }

    public Type MessageClrType => typeof(T);

    void IMessageCodec.WriteErased(BinaryWriter writer, EngineMessage message)
    {
        var typed = (T)message;
        Write(writer, typed);
        PostWrite?.Invoke(typed);
    }

    EngineMessage IMessageCodec.ReadErased(BinaryReader reader)
    {
        var value = Read(reader);
        if (PostRead is not null)
            value = PostRead(value);
        return value;
    }
}

public interface IMessageCodec
{
    MessageType Type { get; }
    ushort WireVersion { get; }
    Type MessageClrType { get; }
    ImmutableArray<FieldDescriptor> Fields { get; }
    void WriteErased(BinaryWriter writer, EngineMessage message);
    EngineMessage ReadErased(BinaryReader reader);
}

public readonly record struct FieldDescriptor(
    int Index,
    string Name,
    WireType Type,
    bool Nullable);

public enum WireType : byte
{
    Bool, Byte, Int16, Int32, Int64, UInt16, UInt32,
    String, NullableString,
    ByteArray, NullableByteArray, SensitiveBytes,
    Enum, RecordArray
}
```

### Registry

```csharp
namespace FalkForge.Engine.Protocol.Serialization;

/// <summary>
/// Static registry mapping (MessageType, WireVersion) pairs to codec instances.
/// Built once at type initialization. Frozen dictionary for O(1) hot-path lookup.
///
/// Version negotiation: ForRead first tries exact match, then falls back to
/// the nearest lower wire version for the same MessageType. Enables rolling
/// upgrade where a v2 client and v1 server can talk if v2 only adds trailing
/// optional fields to existing messages.
/// </summary>
public static class MessageCodecRegistry
{
    private static readonly FrozenDictionary<CodecKey, IMessageCodec> s_codecs;
    private static readonly FrozenDictionary<Type, IMessageCodec> s_byClrType;

    public const ushort CurrentWireVersion = 1;

    static MessageCodecRegistry();

    public static IMessageCodec ForWrite(EngineMessage message);
    public static Result<IMessageCodec> ForRead(MessageType type, ushort wireVersion);
    public static IReadOnlyCollection<IMessageCodec> All { get; }

    private readonly record struct CodecKey(MessageType Type, ushort Version);
}
```

### Secure property fix

```csharp
namespace FalkForge.Engine.Protocol.Messages;

/// <summary>
/// Carries a DPAPI-protected property value across the UI-to-engine pipe.
/// SecureValue is a SensitiveBytes instance that zeros its backing buffer
/// on dispose. Message is IDisposable so callers can deterministically
/// clean up plaintext. Codec PostWrite hook disposes the value after the
/// bytes have been serialized to the wire.
/// </summary>
public sealed record SetSecurePropertyMessage(
    string PropertyName,
    SensitiveBytes SecureValue) : EngineMessage, IDisposable
{
    public override MessageType Type => MessageType.SetSecureProperty;
    public void Dispose() => SecureValue.Dispose();
}
```

### Example codec — `ProgressCodec`

```csharp
namespace FalkForge.Engine.Protocol.Serialization.Codecs;

public static class ProgressCodec
{
    public static readonly MessageCodec<ProgressMessage> V1 = new()
    {
        Type = MessageType.Progress,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor(0, nameof(ProgressMessage.Current), WireType.Int32, false),
            new FieldDescriptor(1, nameof(ProgressMessage.Total), WireType.Int32, false),
            new FieldDescriptor(2, nameof(ProgressMessage.CurrentPackage), WireType.Int32, false),
            new FieldDescriptor(3, nameof(ProgressMessage.PackagePercent), WireType.Int32, false)),
        Write = static (w, m) =>
        {
            w.Write(m.Current);
            w.Write(m.Total);
            w.Write(m.CurrentPackage);
            w.Write(m.PackagePercent);
        },
        Read = static r => new ProgressMessage(
            Current: r.ReadInt32(),
            Total: r.ReadInt32(),
            CurrentPackage: r.ReadInt32(),
            PackagePercent: r.ReadInt32())
    };
}
```

### Example codec — `SetSecurePropertyCodec` with lifecycle

```csharp
namespace FalkForge.Engine.Protocol.Serialization.Codecs;

public static class SetSecurePropertyCodec
{
    public static readonly MessageCodec<SetSecurePropertyMessage> V1 = new()
    {
        Type = MessageType.SetSecureProperty,
        WireVersion = 1,
        Fields = ImmutableArray.Create(
            new FieldDescriptor(0, nameof(SetSecurePropertyMessage.PropertyName), WireType.String, false),
            new FieldDescriptor(1, nameof(SetSecurePropertyMessage.SecureValue), WireType.SensitiveBytes, false)),
        Write = static (w, m) =>
        {
            w.Write(m.PropertyName);
            // Borrow plaintext from SensitiveBytes via a scoped reveal.
            // The reveal returns a pooled buffer that is zeroed and returned
            // as soon as the using scope exits.
            using var reveal = m.SecureValue.Borrow();
            w.Write(reveal.Length);
            w.Write(reveal.Span);
        },
        Read = static r =>
        {
            var name = r.ReadString();
            var length = r.ReadInt32();
            // Rent pooled scratch, read plaintext, wrap in SensitiveBytes
            // immediately, zero the scratch before returning to the pool.
            var scratch = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                r.Read(scratch.AsSpan(0, length));
                var sensitive = SensitiveBytes.FromPlaintext(scratch.AsSpan(0, length));
                return new SetSecurePropertyMessage(name, sensitive);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(scratch.AsSpan(0, length));
                ArrayPool<byte>.Shared.Return(scratch, clearArray: true);
            }
        },
        // Defense-in-depth: dispose the outer message's SensitiveBytes
        // after the write has completed. Caller can still call Dispose
        // explicitly, but PostWrite ensures single-use messages are
        // cleaned up even if the caller forgets.
        PostWrite = static m => m.SecureValue.Dispose()
    };
}
```

Three layers of secure-byte zeroing: the write-side pooled reveal buffer is zeroed on `Dispose`, the read-side scratch buffer is zeroed via `CryptographicOperations.ZeroMemory` plus `ArrayPool.Return(clearArray: true)`, and the message's `SensitiveBytes` wrapper zeros its backing buffer when the message is disposed.

### What the deepened module owns

- `MessageCodec<T>` record as the canonical per-message declaration. Approximately 15-20 LOC per codec, grep-able by name, isolated unit-testable.
- `IMessageCodec` non-generic facade allowing the registry to store heterogeneous codec types in one dictionary.
- `MessageCodecRegistry` with `(MessageType, WireVersion)` composite keys supporting version negotiation.
- `FieldDescriptor` + `WireType` enum metadata supporting test-time field reorder detection via a schema walker.
- `MessageSerializer.Serialize` and `MessageDeserializer.Deserialize` facades preserved for transport-layer callers.
- `SetSecurePropertyMessage` typed as `SensitiveBytes` instead of `byte[]`, implementing `IDisposable`, with three-layer zeroing defense baked into its codec.
- Byte-parity regression test suite comparing every message sample against the legacy serializer output during cutover.
- Field reorder detection test walking each codec's declared `Fields` schema against the emitted bytes.

### What the deepened module hides

- The paired switch statements in `MessageSerializer.cs` and `MessageDeserializer.cs`. Deleted.
- Hand-written field ordering requiring two-file coordination.
- The `SetSecurePropertyMessage` plaintext leak via `byte[]` with no zeroing.
- Per-case hardcoded field encoding patterns.
- The hardcoded `ProtocolVersion = 1` gate with no downgrade path.
- The assumption that all callers want the same wire format (codec records cleanly support version coexistence via multiple static fields).

## Dependency Strategy

This module is **pure in-process byte encoding/decoding**. No ports, no async, no I/O, no external dependencies beyond BCL primitives. The refactor is structural.

### Codec registration

Codecs are static readonly fields per message type in a per-message class under `Serialization/Codecs/`. `MessageCodecRegistry`'s static constructor enumerates them explicitly in a centralized list:

```csharp
static MessageCodecRegistry()
{
    var all = new IMessageCodec[]
    {
        CancelCodec.V1,
        ProgressCodec.V1,
        ErrorCodec.V1,
        PhaseChangedCodec.V1,
        DetectBeginCodec.V1,
        DetectCompleteCodec.V1,
        SetPropertyCodec.V1,
        SetSecurePropertyCodec.V1,
        // ... 20 more codecs
    };

    s_codecs = all.ToFrozenDictionary(c => new CodecKey(c.Type, c.WireVersion));
    s_byClrType = all
        .Where(c => c.WireVersion == CurrentWireVersion)
        .ToFrozenDictionary(c => c.MessageClrType);
}
```

No reflection, no attribute scanning, no runtime codegen. NativeAOT-safe. Registration failures (duplicate keys, type mismatches) surface at process start, not at first message send.

### Version negotiation

Future wire version bumps add new static fields alongside existing ones:

```csharp
public static class ProgressCodec
{
    public static readonly MessageCodec<ProgressMessage> V1 = /* four int32 fields */;

    // V2 adds EstimatedRemainingMs as a trailing field.
    public static readonly MessageCodec<ProgressMessage> V2 = new()
    {
        Type = MessageType.Progress,
        WireVersion = 2,
        Fields = V1.Fields.Add(new FieldDescriptor(4, nameof(ProgressMessage.EstimatedRemainingMs), WireType.Int32, false)),
        Write = static (w, m) => { /* five writes */ },
        Read = static r => new ProgressMessage(
            r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32())
    };
}
```

Both `V1` and `V2` register. Writers always emit at `CurrentWireVersion`. Readers consult `(MessageType.Progress, incomingVersion)` first, falling back to the nearest lower version if exact match fails. Progress v1 bytes arriving at a v2-aware engine deserialize via `V1` with `EstimatedRemainingMs` defaulting to zero in the resulting record.

### Secure property transport

`SetSecurePropertyMessage.SecureValue` changes from `byte[]` to `SensitiveBytes`. The message implements `IDisposable`. Three zeroing layers active on every secure message round-trip:

1. **Write side pooled reveal buffer** — `SensitiveBytes.Borrow()` returns a disposable reveal scope that rents a pooled buffer, exposes its plaintext via a `Span<byte>`, and zeros the buffer plus returns it to the pool on `Dispose`.
2. **Read side scratch buffer** — the codec rents from `ArrayPool<byte>.Shared`, reads plaintext into the scratch, wraps immediately in `SensitiveBytes.FromPlaintext`, zeros the scratch via `CryptographicOperations.ZeroMemory`, and returns to the pool with `clearArray: true` as a second defense.
3. **Message disposal** — `SetSecurePropertyMessage` implements `IDisposable` and disposes its `SensitiveBytes` field when the caller is done with the message. The codec's `PostWrite` hook disposes automatically after serialization so one-shot messages do not leak even if the caller forgets to `using` them.

Plaintext exists only inside the kernel's named-pipe buffer for the duration of the cross-process write. The pipe itself is HMAC-authenticated and bounded to the known child process PID, so the plaintext window is microseconds wide and bounded by pipe security.

### Test substitution

Three test tiers:

1. **Codec unit test** — instantiate one codec directly, round-trip a sample message through `BinaryWriter`/`BinaryReader` against a `MemoryStream`, assert field equality. No registry, no facade, no transport. Fast, isolated.
2. **Registry integration test** — exercise `MessageCodecRegistry.ForRead` and `ForWrite` with known codecs and version combinations. Version fallback behavior testable here.
3. **Transport integration test** — `MessageSerializer.Serialize` + `MessageDeserializer.Deserialize` end-to-end, still using the existing fake `PipeStream` pair from `FalkForge.Testing`.

No mocking framework required.

## Testing Strategy

**Replace, don't layer.** The existing round-trip tests in `MessageSerializerTests.cs` + `MessageDeserializerTests.cs` remain as the safety net during cutover. Per-codec isolated tests are added alongside them. After the byte-parity regression suite goes green, the legacy switch-based serializer is deleted and the old tests are reduced to one-per-message round-trip coverage.

### New boundary tests to write

At the per-codec level (one test class per codec, approximately 3 tests per class, approximately 28 × 3 = 84 tests):

1. **Round-trip equality** — construct a representative sample, write to a `MemoryStream`, read back, assert record equality.
2. **Field schema correctness** — assert the codec's declared `Fields` contains the expected field names in the expected order with the expected `WireType` values.
3. **Edge cases per codec** — nullable strings with null vs empty string vs populated, array-of-records with zero and many elements, enum round-trip preserving value, byte array with empty and populated cases.

At the registry level (approximately 10 tests):

4. **Exact version match** — register codec for `(Progress, 1)`, look up `(Progress, 1)`, assert the same instance is returned.
5. **Version fallback** — register codecs for `(Progress, 1)` and `(Progress, 2)`, look up `(Progress, 3)`, assert the fallback returns the nearest lower (`V2`).
6. **Unknown type rejection** — look up `((MessageType)0xFFFF, 1)`, assert `Result.Failure`.
7. **Duplicate registration detection** — static constructor fails if two codecs claim the same `(Type, WireVersion)` pair.
8. **CLR type dispatch** — `ForWrite` given a `ProgressMessage` returns the `Progress` codec.
9. **Zero-alloc on hot path** — `GC.GetAllocatedBytesForCurrentThread()` before/after a `Serialize` call on a `ProgressMessage`, assert allocation is bounded to the single output `byte[]`.

At the byte-parity level (28 messages × 1 test = 28 tests, retained until legacy deletion):

10. **Byte-identical output per sample** — `MessageSamples.All()` canonical instances are serialized through both the new registry path and the kept-alive `LegacyMessageSerializer`, bytes compared for equality. Cutover PR cannot merge until this is green for every message.
11. **Legacy bytes deserialize via new reader** — `LegacyMessageSerializer.Serialize(sample)` output is fed into `MessageDeserializer.Deserialize` (new implementation), assert the decoded message equals the sample.

At the field reorder detection level (28 tests):

12. **Declared field order matches wire emission order** — `FieldInterpreter.Walk(codec.Fields)` over the bytes emitted by `codec.Write` consumes exactly the emitted bytes. Any reorder between the `Fields` schema and the `Write` delegate causes the walker to leave unconsumed bytes or run out of bytes early, failing the test with a structured error naming the offending codec and field index.

At the secure property level (approximately 5 tests):

13. **Secure round-trip preserves plaintext** — encode a known plaintext, decode, assert the `SensitiveBytes.Borrow().Span` matches the original.
14. **Secure dispose zeros backing buffer** — decode a secure message, dispose it, assert the underlying buffer contains only zero bytes (via a test-only `SensitiveBytes.WasZeroed` property or a recording factory).
15. **Write-side reveal buffer zeroed after Write** — use a recording `ArrayPool<byte>` wrapper that captures returned buffers, assert returned buffers contain only zero bytes.
16. **PostWrite hook disposes outer message** — codec wraps a test `SensitiveBytes`, after `MessageSerializer.Serialize` completes assert the original `SensitiveBytes` is disposed.
17. **No plaintext lingers in log output** — serialize a `SetSecurePropertyMessage` with plaintext `"hunter2"`, assert the bytes `"hunter2"` do not appear in any accessible log buffer.

### Old tests to delete

- After cutover: the giant switch cases in `MessageSerializerTests.cs` and `MessageDeserializerTests.cs` are reduced to one round-trip per message, delegating the details to the per-codec tests.
- `LegacyMessageSerializer` and its tests are deleted in a follow-up commit one release cycle after the cutover PR merges.

### Test environment needs

- No new NuGet packages.
- `MessageSamples.All()` central fixture collection — one canonical instance per message type with representative field values including nullable fields, arrays, and secure bytes where applicable.
- `FieldInterpreter.Walk` helper — approximately 60 LOC utility that reads a `BinaryReader` according to a `FieldDescriptor[]` schema, used only by the reorder detection test.
- Test-only `SensitiveBytes.WasZeroed` property exposed via `InternalsVisibleTo` on the protocol tests assembly, or an equivalent recording factory.

## Implementation Recommendations

Durable architectural guidance, decoupled from current file paths:

### What the module should own

- `MessageCodec<T>` as the canonical per-message record. One static readonly field per message per wire version, grep-able and individually testable.
- `MessageCodecRegistry` as the immutable `FrozenDictionary`-backed lookup keyed by `(MessageType, WireVersion)` with nearest-lower fallback semantics.
- The static `MessageSerializer.Serialize` / `MessageDeserializer.Deserialize` facades preserving call signatures for transport-layer callers.
- Secure property lifecycle — `SensitiveBytes` typing, `IDisposable` on the message, three-layer zeroing defense in the codec.
- Field reorder detection via `FieldInterpreter.Walk` against declared `FieldDescriptor[]` schemas.
- Byte-parity regression test suite running during cutover.
- Version negotiation rules: writers emit at `CurrentWireVersion`, readers accept the exact match or the nearest-lower wire version for the same message type.

### What the module should hide

- The paired switch statements.
- Hand-written per-case field ordering.
- The `ProtocolVersion = 1` hard gate.
- The `SetSecurePropertyMessage.SecureValue` plaintext-as-`byte[]` type.
- The assumption that wire format is implicit in serializer code.

### What the module should expose

Two public surfaces:

1. **Facade** — `MessageSerializer.Serialize(EngineMessage)` and `MessageDeserializer.Deserialize(ReadOnlySpan<byte>)`. Preserved call signatures. Transport layer keeps its existing `PipeTransportBase.SendAsync`/`ReceiveAsync` unchanged.
2. **Codec registry** — `MessageCodecRegistry.All`, `ForWrite`, `ForRead` for tests, future telemetry tooling, future `forge protocol --dump-schema` CLI command, and per-codec isolated testing.

### How callers should migrate

**Transport layer (`PipeTransportBase`)** — no changes. Calls `MessageSerializer.Serialize` and `MessageDeserializer.Deserialize` exactly as today.

**Test callers** migrate from integration-only round-trip tests to per-codec isolated tests plus the byte-parity regression suite during cutover.

**Secure property callers** — code that constructs `SetSecurePropertyMessage` today with a raw `byte[]` must migrate to passing a `SensitiveBytes` instance. Calling code should `using` the message where practical, though the codec's `PostWrite` hook provides a safety net for callers that forget.

**Future codec authors** — one new static class under `Serialization/Codecs/` with a `MessageCodec<T> V1` field, one new line in `MessageCodecRegistry`'s static constructor registration list, plus a per-codec test class and an entry in `MessageSamples.All()`. No switch edits anywhere.

### Implementation sequencing

TDD-driven, each phase gets its own implementation plan file under `docs/plans/`. Sketch of order:

1. **Define core types** — `MessageCodec<T>`, `IMessageCodec`, `FieldDescriptor`, `WireType`. Value types with no behavior. Failing-first test on record construction and `Fields` immutability.
2. **Stand up `MessageCodecRegistry` skeleton** — empty registry, failing-first test that `ForRead` on an empty registry returns `Result.Failure`.
3. **Introduce `LegacyMessageSerializer`** — rename the existing `MessageSerializer`/`MessageDeserializer` static classes to `LegacyMessageSerializer`/`LegacyMessageDeserializer`. Preserve their behavior exactly. All existing tests continue passing against the legacy types.
4. **Add the new `MessageSerializer` facade** — delegates to `MessageCodecRegistry.ForWrite`. Returns same byte layout as legacy. Zero codecs registered yet, so fails on every call. Failing-first test.
5. **Port codecs one at a time, TDD** — start with `CancelCodec` (simplest). Write failing test for the codec, write the static field, register in `MessageCodecRegistry`, verify byte-parity against `LegacyMessageSerializer` for the `Cancel` sample. Then `ProgressCodec`, `PhaseChangedCodec`, `ErrorCodec`, `LogCodec`, `RequestDetectCodec`, `RequestPlanCodec`, `RequestApplyCodec`, `DetectBeginCodec`, `PlanBeginCodec`, `ApplyBeginCodec`, `DetectCompleteCodec`, `PlanCompleteCodec`, `ApplyCompleteCodec`, `SetPropertyCodec`, `SetInstallDirectoryCodec`, `SetFeatureSelectionCodec`, `ShutdownRequestCodec`, `ShutdownResponseCodec`, `UpdateAvailableCodec`, `UpdateReadyCodec`, `UpdateDownloadProgressCodec`, `LaunchUpdateCodec`, `LicenseCodec`, `ElevateExecuteCodec`, `ElevateResultCodec`, `ElevateProgressCodec`. One codec per commit.
6. **Port `SetSecurePropertyCodec` with lifecycle fix** — the critical commit. Change `SetSecurePropertyMessage.SecureValue` type from `byte[]` to `SensitiveBytes`. Add `IDisposable`. Implement the codec with three-layer zeroing defense. Write lifecycle tests: round-trip, dispose zeros backing buffer, reveal buffer zeroed, scratch buffer zeroed, `PostWrite` hook disposes message.
7. **Stand up `FieldInterpreter.Walk` helper** — the schema walker used by reorder detection tests.
8. **Write field reorder detection tests** — one per codec, walks declared `Fields` against emitted bytes, asserts exact byte consumption.
9. **Write the byte-parity regression suite** — `MessageSamples.All()` fixture, one test per sample comparing new serializer output against `LegacyMessageSerializer`. This is the gate that must be green before cutover.
10. **Cutover** — switch `PipeTransportBase` and all other transport callers from `LegacyMessageSerializer` to `MessageSerializer`. Run the full test suite including the byte-parity regression. Green means cutover is safe.
11. **Deprecate `LegacyMessageSerializer`** — mark `[Obsolete]`, retain for one release cycle as a safety valve.
12. **Delete `LegacyMessageSerializer`** — one commit, mechanical. Byte-parity tests converted to per-codec round-trip tests or deleted.
13. **Wire version negotiation tests** — stand up a synthetic `V2` codec for one message, assert `(MessageType, 2)` round-trip works, assert `(MessageType, 1)` still resolves to `V1` via fallback.
14. **Documentation** — update `docs/` with the codec-per-message architecture, the secure property lifecycle diagram, and the "add a new message type" guide.

Each phase of the sequencing plan gets its own implementation plan file under `docs/plans/`, paired with this design document.
