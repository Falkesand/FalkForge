using FalkForge.Engine.Protocol.Messages;
using FalkForge.Engine.Protocol.Serialization;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests;

/// <summary>
/// Deterministic fuzz harness for MessageDeserializer and the codec pipeline.
///
/// The pipe frame format is [wireVersion:u16][type:u16][payloadLength:i32][payload bytes].
/// Both processes (UI and Engine) are co-deployed from the same source tree, but the Engine
/// treats the UI side as less-trusted: a compromised UI process could send crafted frames.
///
/// Invariants verified:
///   1. Never throws an unhandled exception — all inputs return Result.Success or Result.Failure.
///   2. The MaxPayloadSize (1 MiB) guard fires before any large allocation is attempted.
///   3. Negative payload lengths are rejected without allocating.
///   4. Payload truncation (claimed > available bytes) is caught before codec reads.
///
/// Seeds are fixed for determinism. To reproduce a CI failure:
///   Copy the seed and iteration from the assertion message, reconstruct the byte array as:
///     var rng = new Random(seed); for (var i = 0; i &lt; failingIteration; i++) { ... skip ... }
///   Or use the hex prefix directly: MessageDeserializer.Deserialize(Convert.FromHexString(hex)).
///
/// Scale up:
///   FALKFORGE_FUZZ_ITERATIONS=50000 dotnet test --filter "MessageDeserializerFuzz"
/// </summary>
public sealed class MessageDeserializerFuzzTests
{
    private static readonly int Iterations =
        int.TryParse(Environment.GetEnvironmentVariable("FALKFORGE_FUZZ_ITERATIONS"), out var n)
            ? n : 400;

    // Collect a baseline of valid serialized messages to use as mutation seeds.
    private static readonly byte[][] ValidBaselines = BuildValidBaselines();

    private static byte[][] BuildValidBaselines()
    {
        var messages = new EngineMessage[]
        {
            new DetectBeginMessage { SequenceId = 0u },
            new DetectCompleteMessage
            {
                SequenceId = 1u,
                State = InstallState.NotInstalled,
                Features = []
            },
            new RequestDetectMessage { SequenceId = 2u },
            new RequestApplyMessage { SequenceId = 3u },
            new CancelMessage { SequenceId = 4u },
            new ProgressMessage
            {
                SequenceId = 5u,
                Progress = new InstallProgress(42, 100, "Installing", 42)
            },
            new LogMessage
            {
                SequenceId = 6u,
                Level = LogLevel.Info,
                Text = "test"
            },
            new PhaseChangedMessage
            {
                SequenceId = 7u,
                Phase = EnginePhase.Applying
            },
            new ErrorMessage
            {
                SequenceId = 8u,
                Message = "err",
                Kind = ErrorKind.Validation
            },
        };
        return messages.Select(MessageSerializer.Serialize).ToArray();
    }

    /// <summary>
    /// Random byte sequences — fully random garbage — must never cause Deserialize to throw.
    /// Every random buffer must return Success or Failure.
    /// </summary>
    [Fact]
    public void Deserialize_RandomGarbage_NeverThrows()
    {
        var rng = new Random(unchecked((int)0xFACC_1001));
        var failures = new List<string>();

        for (var i = 0; i < Iterations; i++)
        {
            var length = rng.Next(0, 256);
            var data = new byte[length];
            rng.NextBytes(data);

            try
            {
                var result = MessageDeserializer.Deserialize(data);
                _ = result.IsSuccess || result.IsFailure;
            }
            catch (Exception ex)
            {
                failures.Add(
                    $"Iteration {i} (seed=0xFACC1001, length={length}): " +
                    $"hex={Convert.ToHexString(data[..Math.Min(32, data.Length)])}... " +
                    $"threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.Empty(failures);
    }

    /// <summary>
    /// Valid message bytes with random bit-flips in the payload section must never throw.
    /// Bit-flips in length fields, type fields, and sequence IDs are all exercised.
    /// </summary>
    [Fact]
    public void Deserialize_BitFlippedValidMessages_NeverThrows()
    {
        var rng = new Random(unchecked((int)0xFACC_1002));
        var failures = new List<string>();

        for (var i = 0; i < Iterations; i++)
        {
            var baseline = ValidBaselines[i % ValidBaselines.Length];
            var mutated = MutateBitFlips(rng, baseline, flips: rng.Next(1, Math.Max(2, baseline.Length / 4)));

            try
            {
                var result = MessageDeserializer.Deserialize(mutated);
                _ = result.IsSuccess || result.IsFailure;
            }
            catch (Exception ex)
            {
                failures.Add(
                    $"Iteration {i} (seed=0xFACC1002): " +
                    $"baseline_hex={Convert.ToHexString(baseline[..Math.Min(16, baseline.Length)])} " +
                    $"mutated_hex={Convert.ToHexString(mutated[..Math.Min(16, mutated.Length)])} " +
                    $"threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.Empty(failures);
    }

    /// <summary>
    /// Truncated valid messages (0..N bytes of a valid frame) must never throw.
    /// Tests the "payload truncated" guard and short-header guard simultaneously.
    /// </summary>
    [Fact]
    public void Deserialize_TruncatedValidMessages_NeverThrows()
    {
        var rng = new Random(unchecked((int)0xFACC_1003));
        var failures = new List<string>();

        for (var i = 0; i < Iterations; i++)
        {
            var baseline = ValidBaselines[i % ValidBaselines.Length];
            var cutAt = rng.Next(0, baseline.Length + 1);
            var truncated = baseline[..cutAt];

            try
            {
                var result = MessageDeserializer.Deserialize(truncated);
                _ = result.IsSuccess || result.IsFailure;
            }
            catch (Exception ex)
            {
                failures.Add(
                    $"Iteration {i} (seed=0xFACC1003, cutAt={cutAt}): " +
                    $"threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.Empty(failures);
    }

    /// <summary>
    /// Frames with crafted payloadLength fields (large positive, negative, Int32.Max)
    /// must be rejected by the MaxPayloadSize guard before any allocation attempt.
    /// This is the primary allocation-DoS attack class for this parser.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(-2147483648)]   // Int32.MinValue
    [InlineData(1048577)]       // MaxPayloadSize + 1
    [InlineData(2147483647)]    // Int32.MaxValue
    [InlineData(100_000_000)]   // 100 MB
    public void Deserialize_LargeOrNegativePayloadLength_ReturnsFailureNotThrows(int payloadLength)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((ushort)1);                           // wireVersion
        bw.Write((ushort)MessageType.DetectBegin);     // type
        bw.Write(payloadLength);                       // crafted length
        bw.Write(0u);                                  // sequenceId (4 bytes actual)
        var data = ms.ToArray();

        Result<EngineMessage> result;
        try
        {
            result = MessageDeserializer.Deserialize(data);
        }
        catch (Exception ex)
        {
            Assert.Fail($"payloadLength={payloadLength} caused unhandled {ex.GetType().Name}: {ex.Message}");
            return;
        }

        Assert.True(result.IsFailure,
            $"payloadLength={payloadLength} should be rejected but Deserialize returned Success.");
    }

    /// <summary>
    /// Known-good round-trip: serialize then deserialize every baseline message.
    /// Verifies fuzz mutations don't accidentally break the happy path.
    /// </summary>
    [Fact]
    public void Deserialize_ValidRoundTrip_AllBaselineMessages_Succeed()
    {
        var messages = new EngineMessage[]
        {
            new DetectBeginMessage { SequenceId = 1u },
            new CancelMessage { SequenceId = 2u },
            new RequestDetectMessage { SequenceId = 3u },
            new LogMessage
            {
                SequenceId = 4u,
                Level = LogLevel.Info,
                Text = "ok"
            },
        };

        foreach (var msg in messages)
        {
            var bytes = MessageSerializer.Serialize(msg);
            var result = MessageDeserializer.Deserialize(bytes);
            Assert.True(result.IsSuccess,
                $"Round-trip failed for {msg.GetType().Name}: " +
                (result.IsFailure ? result.Error.Message : ""));
        }
    }

    /// <summary>
    /// Pinned regression for the bit-flip frame that escaped the codec read guard
    /// (fuzz seed 0xFACC1002, iteration 105, LogMessage baseline). The corrupting
    /// flips shift the length-prefixed Text field so that fewer than 16 bytes remain
    /// for the trailing 16-byte SessionCorrelationId GUID. <c>new Guid(byte[])</c>
    /// then throws <see cref="ArgumentException"/> ("Byte array for Guid must be
    /// exactly 16 bytes long"), which the deserializer's original catch filter
    /// (<see cref="IOException"/>/<see cref="EndOfStreamException"/>/
    /// <see cref="InvalidOperationException"/> only) did not cover, so it escaped as
    /// an unhandled throw. The facade contract is "never throws for malformed wire
    /// input" — this frame must return a typed <see cref="Result{T}"/> failure.
    /// </summary>
    [Fact]
    public void Deserialize_LogFrameCorruptingGuidLength_ReturnsFailureNotThrows()
    {
        // Exact bytes produced by the fuzz harness at seed 0xFACC1002, iteration 105.
        var malformed = Convert.FromHexString(
            "02000A011D000000060000020C74677374020000000000000800C000000400000000004000");

        Result<EngineMessage> result;
        try
        {
            result = MessageDeserializer.Deserialize(malformed);
        }
        catch (Exception ex)
        {
            Assert.Fail(
                $"Malformed Log frame must not throw; deserializer leaked {ex.GetType().Name}: {ex.Message}");
            return;
        }

        Assert.True(result.IsFailure,
            "Corrupted Log frame should be rejected as a typed failure, not parsed.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static byte[] MutateBitFlips(Random rng, byte[] input, int flips)
    {
        var data = (byte[])input.Clone();
        for (var i = 0; i < flips; i++)
        {
            var idx = rng.Next(data.Length);
            data[idx] ^= (byte)(1 << rng.Next(8));
        }
        return data;
    }
}
