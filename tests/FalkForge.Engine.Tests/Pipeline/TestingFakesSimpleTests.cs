namespace FalkForge.Engine.Tests.Pipeline;

using FalkForge.Engine.Logging;
using FalkForge.Engine.Protocol;
using FalkForge.Engine.RestartManager;
using FalkForge.Testing;
using Xunit;

/// <summary>
/// Contract tests for the simple testing fakes:
/// <see cref="FakeClock"/>, <see cref="DeterministicRandom"/>,
/// <see cref="ListLogger"/>, and <see cref="NullRestartManager"/>.
/// </summary>
public sealed class TestingFakesSimpleTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // FakeClock
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FakeClock_StartsAtDefaultEpoch_WhenNoStartTimeGiven()
    {
        var clock = new FakeClock();
        var expected = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(expected, clock.UtcNow);
    }

    [Fact]
    public void FakeClock_Advance_MovesTimeForward()
    {
        var clock = new FakeClock();
        clock.Advance(TimeSpan.FromHours(1));
        var expected = new DateTimeOffset(2024, 1, 1, 1, 0, 0, TimeSpan.Zero);
        Assert.Equal(expected, clock.UtcNow);
    }

    [Fact]
    public void FakeClock_Set_OverridesCurrentTime()
    {
        var clock = new FakeClock();
        var target = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);
        clock.Set(target);
        Assert.Equal(target, clock.UtcNow);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // DeterministicRandom
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DeterministicRandom_NewGuid_ReturnsDistinctValues()
    {
        var rng = new DeterministicRandom();
        var g1 = rng.NewGuid();
        var g2 = rng.NewGuid();
        Assert.NotEqual(g1, g2);
    }

    [Fact]
    public void DeterministicRandom_Fill_WritesFillByte()
    {
        var rng = new DeterministicRandom(fillByte: 0x42);
        Span<byte> buf = stackalloc byte[8];
        rng.Fill(buf);
        Assert.True(buf.SequenceEqual(stackalloc byte[8] { 0x42, 0x42, 0x42, 0x42, 0x42, 0x42, 0x42, 0x42 }));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ListLogger
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ListLogger_AccumulatesEntries()
    {
        var logger = new ListLogger();
        logger.Info("cat", "msg1");
        logger.Warning("cat", "msg2");
        Assert.Equal(2, logger.Entries.Count);
    }

    [Fact]
    public void ListLogger_RespectsMinimumLevel()
    {
        var logger = new ListLogger { MinimumLevel = LogLevel.Warning };
        logger.Debug("cat", "ignored");
        logger.Info("cat", "also ignored");
        logger.Warning("cat", "kept");
        logger.Error("cat", "also kept");
        Assert.Equal(2, logger.Entries.Count);
    }

    [Fact]
    public void ListLogger_Clear_RemovesAllEntries()
    {
        var logger = new ListLogger();
        logger.Info("cat", "msg");
        logger.Clear();
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void ListLogger_EntriesAt_FiltersCorrectly()
    {
        var logger = new ListLogger();
        logger.Debug("c", "d");
        logger.Error("c", "e");
        var errors = logger.EntriesAt(LogLevel.Error);
        Assert.Single(errors);
        Assert.Equal("e", errors[0].Message);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // NullRestartManager
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NullRestartManager_AllMethodsSucceed()
    {
        using var rm = new NullRestartManager();
        Assert.True(rm.StartSession().IsSuccess);
        Assert.True(rm.RegisterResources(["file1.dll"]).IsSuccess);
        var affected = rm.GetAffectedProcesses();
        Assert.True(affected.IsSuccess);
        Assert.Empty(affected.Value);
        Assert.True(rm.ShutdownProcesses().IsSuccess);
        Assert.True(rm.RestartProcesses().IsSuccess);
        rm.EndSession(); // no throw
    }
}
