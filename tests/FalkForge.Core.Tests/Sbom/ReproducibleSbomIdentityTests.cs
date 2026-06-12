using FalkForge.Sbom;
using Xunit;

namespace FalkForge.Core.Tests.Sbom;

/// <summary>
/// A reproducible build must produce a byte-identical SBOM sidecar across runs. The two
/// fields that otherwise vary per run — the document SerialNumber (a UUID) and the metadata
/// Timestamp — must therefore become deterministic functions of the build inputs whenever
/// SOURCE_DATE_EPOCH is set (the canonical reproducible-build signal). When the epoch is
/// absent the build is not claiming reproducibility, so fresh values are correct and expected.
/// These tests pin that contract: identical content + identical epoch ⇒ identical identity;
/// different content ⇒ different serial; no epoch ⇒ fresh (non-deterministic) values.
/// </summary>
public sealed class ReproducibleSbomIdentityTests : IDisposable
{
    private readonly string? _originalEpoch =
        Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH");

    private static SbomComponent Component(string name, string hash) => new()
    {
        Name = name,
        Version = "1.0.0",
        Type = SbomComponentType.File,
        Sha256Hash = hash
    };

    [Fact]
    public void Resolve_WithEpoch_SameContent_ProducesIdenticalIdentity()
    {
        Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", "1700000000");
        var components = new[] { Component("a.dll", "AAAA"), Component("b.dll", "BBBB") };

        var first = ReproducibleSbomIdentity.Resolve(components, "Prod", "1.0.0");
        var second = ReproducibleSbomIdentity.Resolve(components, "Prod", "1.0.0");

        Assert.Equal(first.SerialNumber, second.SerialNumber);
        Assert.Equal(first.Timestamp, second.Timestamp);
    }

    [Fact]
    public void Resolve_WithEpoch_TimestampComesFromEpoch()
    {
        Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", "1700000000");
        var components = new[] { Component("a.dll", "AAAA") };

        var identity = ReproducibleSbomIdentity.Resolve(components, "Prod", "1.0.0");

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), identity.Timestamp);
    }

    [Fact]
    public void Resolve_WithEpoch_DifferentContent_ProducesDifferentSerial()
    {
        Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", "1700000000");
        var one = new[] { Component("a.dll", "AAAA") };
        var two = new[] { Component("a.dll", "CCCC") }; // same name, different hash

        var first = ReproducibleSbomIdentity.Resolve(one, "Prod", "1.0.0");
        var second = ReproducibleSbomIdentity.Resolve(two, "Prod", "1.0.0");

        Assert.NotEqual(first.SerialNumber, second.SerialNumber);
    }

    [Fact]
    public void Resolve_WithEpoch_ComponentOrderDoesNotChangeSerial()
    {
        // The serial digests the *set* of components, so re-ordering the same components
        // (e.g. a non-deterministic enumeration upstream) must not change the identity.
        Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", "1700000000");
        var forward = new[] { Component("a.dll", "AAAA"), Component("b.dll", "BBBB") };
        var reversed = new[] { Component("b.dll", "BBBB"), Component("a.dll", "AAAA") };

        var first = ReproducibleSbomIdentity.Resolve(forward, "Prod", "1.0.0");
        var second = ReproducibleSbomIdentity.Resolve(reversed, "Prod", "1.0.0");

        Assert.Equal(first.SerialNumber, second.SerialNumber);
    }

    [Fact]
    public void Resolve_WithEpoch_SerialIsUrnUuid()
    {
        Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", "1700000000");
        var components = new[] { Component("a.dll", "AAAA") };

        var identity = ReproducibleSbomIdentity.Resolve(components, "Prod", "1.0.0");

        Assert.StartsWith("urn:uuid:", identity.SerialNumber, StringComparison.Ordinal);
        Assert.True(Guid.TryParse(identity.SerialNumber.AsSpan("urn:uuid:".Length), out _));
    }

    [Fact]
    public void Resolve_WithoutEpoch_ProducesFreshSerialsEachCall()
    {
        Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", null);
        var components = new[] { Component("a.dll", "AAAA") };

        var first = ReproducibleSbomIdentity.Resolve(components, "Prod", "1.0.0");
        var second = ReproducibleSbomIdentity.Resolve(components, "Prod", "1.0.0");

        // Not a reproducible build: fresh GUID per call is correct.
        Assert.NotEqual(first.SerialNumber, second.SerialNumber);
    }

    public void Dispose() =>
        Environment.SetEnvironmentVariable("SOURCE_DATE_EPOCH", _originalEpoch);
}
