using FalkForge.Engine.Protocol.Dependencies;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Engine.Protocol.Tests.Dependencies;

/// <summary>
/// Wire payload for the elevated <c>DependencyRegistration</c> command: the engine (non-elevated for
/// PerUser, but always untrusted-write from the elevated companion's point of view) serializes providers
/// + consumers it wants registered/unregistered under <c>HKLM</c>; the companion deserializes and
/// re-validates before touching the registry. A round-trip must be lossless and a malformed/truncated/
/// oversized blob must be rejected rather than throwing or over-reading.
/// </summary>
public sealed class DependencyRegistrationPayloadTests
{
    private static readonly Guid BundleId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void RoundTrip_Register_PreservesOpcodeBundleIdProvidersAndConsumers()
    {
        var providers = new[] { new ManifestDependencyProvider("SharedLib", "1.2.3", "Shared Library") };
        var consumers = new[] { new ManifestDependencyConsumer("SharedLib", "AppA") };

        var payload = DependencyRegistrationPayload.Serialize(
            DependencyRegistrationOpcode.Register, BundleId, providers, consumers);

        var ok = DependencyRegistrationPayload.TryDeserialize(
            payload, out var opcode, out var bundleId, out var parsedProviders, out var parsedConsumers);

        Assert.True(ok);
        Assert.Equal(DependencyRegistrationOpcode.Register, opcode);
        Assert.Equal(BundleId, bundleId);
        Assert.Single(parsedProviders);
        Assert.Equal("SharedLib", parsedProviders[0].Key);
        Assert.Equal("1.2.3", parsedProviders[0].Version);
        Assert.Equal("Shared Library", parsedProviders[0].DisplayName);
        Assert.Single(parsedConsumers);
        Assert.Equal("SharedLib", parsedConsumers[0].ProviderKey);
        Assert.Equal("AppA", parsedConsumers[0].ConsumerKey);
    }

    [Fact]
    public void RoundTrip_Unregister_PreservesOpcodeAndConsumersWithNoProviders()
    {
        var consumers = new[] { new ManifestDependencyConsumer("SharedLib", "AppA") };

        var payload = DependencyRegistrationPayload.Serialize(
            DependencyRegistrationOpcode.Unregister, BundleId, [], consumers);

        var ok = DependencyRegistrationPayload.TryDeserialize(
            payload, out var opcode, out var bundleId, out var parsedProviders, out var parsedConsumers);

        Assert.True(ok);
        Assert.Equal(DependencyRegistrationOpcode.Unregister, opcode);
        Assert.Equal(BundleId, bundleId);
        Assert.Empty(parsedProviders);
        Assert.Single(parsedConsumers);
    }

    [Fact]
    public void RoundTrip_ProviderWithNullDisplayName_Preserved()
    {
        var providers = new[] { new ManifestDependencyProvider("SharedLib", "1.0.0", null) };

        var payload = DependencyRegistrationPayload.Serialize(
            DependencyRegistrationOpcode.Register, BundleId, providers, []);

        Assert.True(DependencyRegistrationPayload.TryDeserialize(
            payload, out _, out _, out var parsedProviders, out _));
        Assert.Null(parsedProviders[0].DisplayName);
    }

    [Fact]
    public void TryDeserialize_TruncatedBlob_FailsGracefully()
    {
        var payload = DependencyRegistrationPayload.Serialize(
            DependencyRegistrationOpcode.Register, BundleId,
            [new ManifestDependencyProvider("SharedLib", "1.0.0", "Shared Library")], []);
        var truncated = payload[..(payload.Length - 2)];

        Assert.False(DependencyRegistrationPayload.TryDeserialize(truncated, out _, out _, out _, out _));
    }

    [Fact]
    public void TryDeserialize_Empty_Fails()
    {
        Assert.False(DependencyRegistrationPayload.TryDeserialize([], out _, out _, out _, out _));
    }

    [Fact]
    public void TryDeserialize_InvalidOpcodeByte_Fails()
    {
        var payload = DependencyRegistrationPayload.Serialize(
            DependencyRegistrationOpcode.Register, BundleId, [], []);
        payload[0] = 0xFF;

        Assert.False(DependencyRegistrationPayload.TryDeserialize(payload, out _, out _, out _, out _));
    }

    [Fact]
    public void TryDeserialize_TrailingGarbage_Fails()
    {
        var payload = DependencyRegistrationPayload.Serialize(
            DependencyRegistrationOpcode.Register, BundleId, [], []);
        var withGarbage = payload.Concat(new byte[] { 1, 2, 3 }).ToArray();

        Assert.False(DependencyRegistrationPayload.TryDeserialize(withGarbage, out _, out _, out _, out _));
    }
}
