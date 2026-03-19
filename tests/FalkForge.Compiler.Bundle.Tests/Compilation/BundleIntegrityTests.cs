using System.Text.Json;
using FalkForge.Engine.Protocol.Manifest;
using Xunit;

namespace FalkForge.Compiler.Bundle.Tests.Compilation;

public sealed class BundleIntegrityTests
{
    [Fact]
    public void Manifest_WithIntegrityData_SerializesCorrectly()
    {
        var manifest = CreateManifest(
            manifestSignature: "{\"sig\":\"test-signature\"}",
            sbomAttestation: "{\"att\":\"test-attestation\"}");

        var json = JsonSerializer.Serialize(manifest);

        Assert.Contains("ManifestSignature", json);
        Assert.Contains("test-signature", json);
        Assert.Contains("SbomAttestation", json);
        Assert.Contains("test-attestation", json);
    }

    [Fact]
    public void Manifest_WithoutIntegrityData_SerializesWithNulls()
    {
        var manifest = CreateManifest(
            manifestSignature: null,
            sbomAttestation: null);

        var json = JsonSerializer.Serialize(manifest);
        var deserialized = JsonSerializer.Deserialize<InstallerManifest>(json);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.ManifestSignature);
        Assert.Null(deserialized.SbomAttestation);
    }

    [Fact]
    public void Manifest_WithIntegrityData_RoundTripsCorrectly()
    {
        const string sig = "{\"keyid\":\"abc\",\"sig\":\"deadbeef\"}";
        const string att = "{\"payloadType\":\"application/vnd.in-toto+json\"}";

        var manifest = CreateManifest(manifestSignature: sig, sbomAttestation: att);

        var json = JsonSerializer.Serialize(manifest);
        var deserialized = JsonSerializer.Deserialize<InstallerManifest>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(sig, deserialized.ManifestSignature);
        Assert.Equal(att, deserialized.SbomAttestation);
    }

    [Fact]
    public void Manifest_WithoutIntegrityData_IsBackwardCompatible()
    {
        // Simulate a manifest JSON from before integrity fields existed (no integrity keys)
        var oldManifestJson = """
            {
                "Name": "TestBundle",
                "Manufacturer": "TestCo",
                "Version": "1.0.0",
                "BundleId": "00000000-0000-0000-0000-000000000001",
                "UpgradeCode": "00000000-0000-0000-0000-000000000002",
                "Packages": [],
                "Scope": 0
            }
            """;

        var deserialized = JsonSerializer.Deserialize<InstallerManifest>(oldManifestJson);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.ManifestSignature);
        Assert.Null(deserialized.SbomAttestation);
        Assert.Equal("TestBundle", deserialized.Name);
    }

    [Fact]
    public void Manifest_IntegrityFields_PreserveExistingFields()
    {
        var manifest = CreateManifest(
            manifestSignature: "{\"sig\":\"data\"}",
            sbomAttestation: "{\"att\":\"data\"}");

        Assert.Equal("TestBundle", manifest.Name);
        Assert.Equal("TestCo", manifest.Manufacturer);
        Assert.Equal("1.0.0", manifest.Version);
        Assert.Equal("{\"sig\":\"data\"}", manifest.ManifestSignature);
        Assert.Equal("{\"att\":\"data\"}", manifest.SbomAttestation);
    }

    private static InstallerManifest CreateManifest(string? manifestSignature, string? sbomAttestation)
    {
        return new InstallerManifest
        {
            Name = "TestBundle",
            Manufacturer = "TestCo",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Packages = [],
            Scope = InstallScope.PerMachine,
            ManifestSignature = manifestSignature,
            SbomAttestation = sbomAttestation
        };
    }
}
