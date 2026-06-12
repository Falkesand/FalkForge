# Protocol Versioning Policy

**Added:** 2026-06-12 | **Scope:** `src/FalkForge.Engine.Protocol/Serialization/`

## Why this exists

FalkForge ships UI, Engine, and Elevation as a single atomic bundle — a bundle update replaces all three processes together. Until auto-update shipped, the same invariant meant wire compatibility was never a production concern: the three processes always came from the same source tree. Auto-update breaks that invariant in one specific scenario: **the UI that launched the update may be a different version than the Engine that downloads and applies it**. That window lasts for the duration of the apply phase — typically seconds — but it is real.

This document captures what the wire protocol guarantees today, what the single-version contract means in practice, when to bump a version, and what tests enforce these contracts.

---

## Wire frame layout

Every message on the named pipe uses the following framing, written by `MessageSerializer` and read by `MessageDeserializer`:

```
[WireVersion : u16][MessageType : u16][PayloadLength : i32][payload bytes]
```

All integers are little-endian. The header is always 8 bytes. `PayloadLength` is the number of bytes that follow; the deserializer rejects any frame where `PayloadLength < 0` or `PayloadLength > 1 MiB`.

The payload body is written by the codec and begins with `SequenceId (u32)`, followed by type-specific fields in a fixed positional order. **Binary codecs are positional** — there is no field-tag or length-prefix per field (except for string and byte-array fields, which use BinaryWriter's built-in 7-bit length prefix). Adding a field to an existing message **is a breaking layout change** unless the field is appended at the end and the codec explicitly ignores trailing bytes on the read side. The current codecs do **not** use length-guarded reads; adding a field to any codec without bumping its `WireVersion` causes a deserialization failure on the peer.

### FieldDescriptor schema

Each codec carries an immutable `ImmutableArray<FieldDescriptor>` describing its field layout (`Index`, `Name`, `WireType`, `Nullable`). This schema is used by `FieldInterpreter` (introspection and diagnostics) and is enforced by `FieldReorderDetectionTests`. The schema is **not transmitted on the wire** — it is compile-time metadata only.

---

## MessageType ranges

| Range | Direction | Purpose |
|-------|-----------|---------|
| `0x01xx` | Engine → UI | Phase events, progress, errors, updates |
| `0x02xx` | UI → Engine | User commands, property injection |
| `0x03xx` | Engine → Elevated | Commands to the elevated companion |
| `0x04xx` | Elevated → Engine | Results from the elevated companion |

---

## Codec versioning

Each `IMessageCodec` carries a `WireVersion : ushort` that is written into the frame header by `MessageSerializer`. The framing version **equals the codec's `WireVersion`** — there is no separate per-frame version distinct from the per-codec version.

`MessageCodecRegistry.ForRead(type, wireVersion)` resolves the codec:

1. Exact match on `(MessageType, WireVersion)` → use it.
2. No exact match → fall back to the highest registered codec whose version is **≤ wireVersion** for the same type.
3. No codec at any version ≤ wireVersion → return `Result.Failure(ErrorKind.ProtocolError, ...)`.

This means a newer reader can decode frames from an older writer if the older version is still registered. The current production registry does **not** retain old versions of promoted codecs (see Single-Version Contract below).

---

## Verified behavior: unknown type and unknown version

These behaviors were verified by reading `MessageDeserializer` and confirmed by existing tests:

| Scenario | Behavior |
|----------|---------|
| `MessageType` not in any registered codec | `Result.Failure(ErrorKind.ProtocolError)` with message "No codec registered for message type {type} at wire version {version} (or any lower version)" |
| Payload length < 0 or > 1 MiB | `Result.Failure(ErrorKind.ProtocolError)` with message "Invalid payload length: {n}" |
| Buffer shorter than 8 bytes (header) | `Result.Failure(ErrorKind.ProtocolError)` with message "Message too short" |
| Payload truncated (claimed length > actual bytes) | `Result.Failure(ErrorKind.ProtocolError)` with message "Payload truncated" |
| Codec read throws `IOException`, `EndOfStreamException`, or `InvalidOperationException` | `Result.Failure(ErrorKind.Validation)` with message "Codec read failed: {ex.Message}" |
| `ForWrite` called with unregistered CLR type | `InvalidOperationException` thrown (write side is never called with untrusted input; throw is correct) |

**No scenario causes a hang or unhandled exception.** The deserializer never throws for malformed wire input.

---

## Single-version contract

FalkForge bundles ship UI, Engine, and Elevation together as one artifact. An update replaces all three simultaneously. This means:

- **In steady state**, all three processes are always at the same codec version.
- **During auto-update**, the updating Engine may briefly speak to a UI from the previous bundle version (specifically: during the apply phase of a self-update). The update completes and the UI exits before the new Engine restarts, so the cross-version window is bounded.
- The registry intentionally omits old codec versions for message types that have been promoted. For example, `LogCodec` and `PhaseChangedCodec` exist only at `WireVersion = 2`; the removed `WireVersion = 1` codecs are absent. A peer that sends a v1 frame for these types receives a `ProtocolError` failure rather than silently losing the `SessionCorrelationId` field.

**Cross-version interop is not supported** as a general mechanism. If a future requirement (e.g., side-by-side installs, remote engine, third-party host) needs stable cross-version compatibility, the versioning infrastructure must be designed explicitly. The nearest-lower-version fallback in `ForRead` is a foundation for that, but the current registry does not populate old versions.

---

## When to bump WireVersion

| Change type | WireVersion action | Notes |
|-------------|-------------------|-------|
| New message type (new `MessageType` enum value + new codec) | Start at `WireVersion = 1` | Additive; no existing codec affected |
| Add field to existing message (appended to end) | **Bump WireVersion** | Positional binary format; trailing field changes the payload length |
| Remove field from existing message | **Bump WireVersion** | All positional offsets after removal shift |
| Reorder fields | **Bump WireVersion** | Immediate deserialization corruption |
| Change field wire type (e.g., `i32` → `i64`) | **Bump WireVersion** | Width change corrupts all subsequent reads |
| Rename field (no wire change) | No bump | Schema metadata only; wire bytes unaffected |
| Add nullable semantics to existing optional string (empty-sentinel convention) | No bump if sentinel already used | Verify by inspecting the codec's read side |

**When you bump WireVersion:**

1. Register a new codec with the bumped version in `MessageCodecRegistry`. Keep the old version registered only if cross-version fallback is explicitly needed (see Single-Version Contract).
2. Update the `GoldenBytes_wire_format_stable` test in the matching `CodecTests` file to reflect the new layout.
3. Update `MessageCodecRegistryTests.All_contains_exactly_29_registered_codecs` count if a new registration is added.

---

## Additive-safe changes: new message types

Adding a new `MessageType` enum value and a corresponding codec is always additive-safe **provided** the sender is the newer peer and the receiver is the older peer. Under the single-version contract this cannot occur in steady state. During the auto-update window, the updating Engine will not send new message types to the existing UI because the update protocol uses only the messages defined at the time the old UI was built.

**Verified behavior for unknown types:** a receiver that encounters a frame with an unrecognized `MessageType` returns `Result.Failure(ErrorKind.ProtocolError)`. It does **not** crash, hang, or silently skip. The transport layer should then close the connection and report the error to the phase handler.

---

## Field-addition rules per codec

Binary codecs use `BinaryWriter`/`BinaryReader` with fixed positional reads. There is **no self-describing framing per field**. Rules:

1. **Never insert a field in the middle of an existing layout.** All subsequent reads will consume the wrong bytes.
2. **Appending a field at the end requires a WireVersion bump.** The `PayloadLength` in the header changes, and old readers that parse positionally will not consume the extra bytes, leaving them in the stream and corrupting the next message.
3. **String fields** use BinaryWriter's 7-bit-encoded length prefix (variable length). They are safe to contain arbitrary UTF-8 content but are still positional in the codec.
4. **Byte-array fields** use an explicit `i32` length prefix written by the codec (not BinaryWriter's `Write(byte[])` which does not prefix length). See `ElevateExecuteCodec` for the pattern.
5. **Guid fields** are written as exactly 16 raw bytes via `Guid.TryWriteBytes(Span<byte>)` — little-endian, NativeAOT-safe, no boxing.
6. **Nullable fields** are encoded with an empty-string sentinel (for nullable strings) or a presence byte (implementation varies per codec). Always check the codec's `Read` delegate before assuming a field is nullable on the wire.

---

## Compatibility matrix expectations

| Scenario | Expected outcome |
|----------|----------------|
| Same-version UI + Engine | Full compatibility; all codecs match exactly |
| Newer UI + older Engine | Not supported; single-version contract |
| Older UI + newer Engine (auto-update window) | Tolerated for the duration of the apply phase; new message types not sent to old UI |
| Third-party host implementing the protocol | Not supported; no stability guarantee for external consumers in the current release |

---

## Test contract

### Wire-stability tests (golden bytes)

**Every registered codec must have at least one `GoldenBytes_wire_format_stable` test** that serializes a fully-populated, deterministic message instance and asserts the result against a hardcoded expected byte array. These tests make any accidental layout change a build-breaking event.

Coverage as of 2026-06-12: **29/29 codecs** have golden-byte tests.

The meta-gate is `MessageCodecRegistryTests.All_contains_exactly_29_registered_codecs`. When a new codec is added:

1. The count test fails → author is reminded to add a golden-byte test.
2. Author adds `GoldenBytes_wire_format_stable` to the new `*CodecTests.cs` file.
3. Author bumps the count in the meta-test.

The hex constants in golden-byte tests were computed by serializing the canonical message instance and recording the output with `Convert.ToHexString`. Do not update a golden-byte constant without also bumping the codec's `WireVersion` — updating the constant alone hides the breakage.

### Round-trip tests

`MessageRoundtripTests` provides semantic coverage (field values survive serialize→deserialize). These tests complement golden-byte tests but do not replace them: a round-trip test passes even if the field order is swapped and both sides are broken in the same way.

### Unknown-input tests

`MessageRoundtripTests` and `MessageDeserializerFacadeTests` cover the graceful-failure paths:

- Too-short buffer → `ProtocolError`
- Unknown type → `ProtocolError`
- Truncated payload → `ProtocolError`
- Negative payload length → `ProtocolError`
- Payload exceeding 1 MiB → `ProtocolError`

### Version negotiation tests

`VersionNegotiationTests` covers exact-version match, nearest-lower-version fallback, and failure for types with no registered version ≤ requested.

---

## Adding a new message type (checklist)

1. Add `MessageType` enum value in the correct direction range (`0x01xx`–`0x04xx`).
2. Create `Messages/{Name}Message.cs` extending `EngineMessage`.
3. Create `Serialization/Codecs/{Name}Codec.cs` with `WireVersion = 1`.
4. Register `{Name}Codec.Instance` in `MessageCodecRegistry`.
5. Add `tests/…/Serialization/Codecs/{Name}CodecTests.cs` with:
   - `Codec_type_and_version_correct`
   - `RoundTrip_preserves_all_fields`
   - `GoldenBytes_wire_format_stable` (compute hex from `MessageSerializer.Serialize`, paste as constant)
6. Bump the count in `MessageCodecRegistryTests.All_contains_exactly_29_registered_codecs`.
7. Add round-trip coverage in `MessageRoundtripTests` if the message has non-trivial fields.
