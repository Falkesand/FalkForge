using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Docker.DotNet;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FalkForge.Compiler.Bundle;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Engine.Protocol.Integrity;
using FalkForge.Engine.Protocol.Manifest;
using FalkForge.Models;
using FalkForge.Signing;
using FalkForge.Signing.SignServer;
using Xunit;

namespace FalkForge.Integration.Tests;

/// <summary>
/// End-to-end proof that <see cref="SignServerSignatureProvider"/> signs a real FalkForge bundle against a
/// live Keyfactor SignServer instance and that the resulting manifest verifies with the real engine codec.
/// The whole flow runs against a throwaway <c>keyfactor/signserver-ce</c> container: the test provisions a
/// KeystoreCryptoToken + PlainSigner (ECDSA prime256v1, SHA256withECDSA, NOAUTH) worker, builds+signs a
/// bundle through <see cref="BundleCompiler.CompileAsync"/> pointed at the container, then verifies the
/// embedded envelope through <see cref="IntegrityEnvelopeCodec"/>.
///
/// <para><b>Docker/Podman gate.</b> Skips (never fails) when no container runtime is resolvable, so the
/// default <c>dotnet test</c> stays green on machines without one. When a runtime is present it runs for real.
/// A Podman named pipe is auto-wired to <c>DOCKER_HOST</c> and Ryuk is disabled (rootless-Podman friendly).</para>
///
/// <para><b>Confirmed against SignServer CE 7.3.2 (research open items closed live):</b>
/// (1) the REST <c>/process</c> response <c>data</c> field is an ASN.1 <b>DER</b> ECDSA signature
/// (SEQUENCE, first byte 0x30) — the provider converts it to P1363; (2) <c>signerCertificate</c> is a
/// <b>base64-DER</b> X.509 certificate; (3) the worker provisioning that works is a KeystoreCryptoToken over
/// the shipped <c>dss10_keystore.p12</c> (password <c>foo123</c>) plus a PlainSigner whose DEFAULTKEY is the
/// keystore's secp256r1 key <c>apk00002</c> with SIGNATUREALGORITHM=SHA256withECDSA and AUTHTYPE=NOAUTH —
/// applied via <c>bin/signserver setproperties</c> + <c>reload all</c>.</para>
/// </summary>
[Trait("Category", "E2E")]
public sealed class SignServerPodSigningE2ETests
{
    private const int SignServerHttpPort = 8080;
    private const string WorkerName = "PlainECDSA";

    // The exact provisioning proven live: a keystore crypto token + a NOAUTH PlainSigner over the shipped
    // secp256r1 key apk00002, signing SHA256withECDSA. Newlines are materialized by `printf %b` in-container.
    private const string WorkerProperties =
        "WORKER10.TYPE=CRYPTO_WORKER\\n" +
        "WORKER10.IMPLEMENTATION_CLASS=org.signserver.server.signers.CryptoWorker\\n" +
        "WORKER10.CRYPTOTOKEN_IMPLEMENTATION_CLASS=org.signserver.server.cryptotokens.KeystoreCryptoToken\\n" +
        "WORKER10.NAME=CryptoTokenP12\\n" +
        "WORKER10.KEYSTORETYPE=PKCS12\\n" +
        "WORKER10.KEYSTOREPATH=/opt/keyfactor/signserver/res/test/dss10/dss10_keystore.p12\\n" +
        "WORKER10.KEYSTOREPASSWORD=foo123\\n" +
        "WORKER11.TYPE=PROCESSABLE\\n" +
        "WORKER11.IMPLEMENTATION_CLASS=org.signserver.module.cmssigner.PlainSigner\\n" +
        "WORKER11.NAME=" + WorkerName + "\\n" +
        "WORKER11.AUTHTYPE=NOAUTH\\n" +
        "WORKER11.CRYPTOTOKEN=CryptoTokenP12\\n" +
        "WORKER11.DEFAULTKEY=apk00002\\n" +
        "WORKER11.SIGNATUREALGORITHM=SHA256withECDSA\\n" +
        "WORKER11.DISABLEKEYUSAGECOUNTER=true\\n";

    /// <summary>
    /// Upper bound on pulling the SignServer image + starting the container. Without a bound a
    /// stalled registry pull or a wedged daemon hangs `dotnet test` indefinitely; with it the test
    /// fails loud with an OperationCanceledException instead.
    /// </summary>
    internal static readonly TimeSpan ContainerStartTimeout = TimeSpan.FromMinutes(10);

    [Fact]
    public async Task Bundle_SignedByLiveSignServerPod_VerifiesWithTheEngineCodec()
    {
        E2EGate.SkipUnlessOptedIn();

        var (runtimeAvailable, reason) = await ContainerRuntime.TryEnsureConfiguredAsync();
        Assert.SkipUnless(runtimeAvailable, $"No Docker/Podman container runtime available: {reason}");

        var container = new ContainerBuilder()
            .WithImage("keyfactor/signserver-ce:latest")
            .WithPortBinding(SignServerHttpPort, assignRandomHostPort: true)
            // The SignServer web root answers 200 once WildFly has deployed the EAR. (The REST /workers
            // endpoint answers 403 without the X-Keyfactor-Requested-With header, which the default
            // "expect 2xx" strategy would never satisfy — so we probe the root instead.)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request
                .ForPort(SignServerHttpPort)
                .ForPath("/signserver/")))
            .Build();

        try
        {
            using (var startTimeout = new CancellationTokenSource(ContainerStartTimeout))
                await container.StartAsync(startTimeout.Token);
            await ProvisionWorkerAsync(container);

            var baseUrl = $"http://{container.Hostname}:{container.GetMappedPublicPort(SignServerHttpPort)}";
            await WaitForWorkerReadyAsync(baseUrl);

            var config = new SignServerConfig
            {
                BaseUrl = baseUrl,
                Worker = WorkerName,
                AuthMode = SignServerAuthMode.None,
                KeyId = "signserver-plainecdsa"
            };

            var tempDir = Path.Combine(Path.GetTempPath(), $"SignServerE2E_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                using var provider = new SignServerSignatureProvider(config);
                var model = BuildModel(tempDir, provider);

                var compileResult = await new BundleCompiler { AllowPlaceholderStub = true }.CompileAsync(model, Path.Combine(tempDir, "out"));
                Assert.True(compileResult.IsSuccess, compileResult.IsFailure ? compileResult.Error.Message : null);

                var manifest = ExtractManifest(compileResult.Value);
                Assert.NotNull(manifest.ManifestSignature);

                var envelope = IntegrityEnvelopeCodec.Parse(manifest.ManifestSignature!);
                Assert.NotNull(envelope);

                // The real engine verifier accepts the envelope: the DER signature the pod produced was
                // converted to P1363 and the signer certificate's public key was extracted correctly.
                Assert.True(IntegrityEnvelopeCodec.VerifySignature(envelope!),
                    "The live-SignServer-signed envelope did not verify through the engine codec.");

                var signature = Assert.Single(envelope!.Signatures);
                Assert.Equal("signserver-plainecdsa", signature.KeyId);
                Assert.NotEmpty(Convert.FromBase64String(signature.PublicKey));

                // Bind check: the signed file entry carries the real payload hash from the manifest.
                var fileEntry = Assert.Single(envelope.Files);
                Assert.Equal(manifest.Packages[0].Sha256Hash, fileEntry.Sha256);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
        }
        finally
        {
            await container.DisposeAsync();
        }
    }

    private static async Task ProvisionWorkerAsync(IContainer container)
    {
        var write = await container.ExecAsync(
            ["sh", "-c", $"printf '%b' \"{WorkerProperties}\" > /tmp/falk-worker.properties"]);
        Assert.Equal(0L, write.ExitCode);

        var apply = await container.ExecAsync(["bin/signserver", "setproperties", "/tmp/falk-worker.properties"]);
        Assert.Equal(0L, apply.ExitCode);

        var reload = await container.ExecAsync(["bin/signserver", "reload", "all"]);
        Assert.Equal(0L, reload.ExitCode);
    }

    /// <summary>Polls the worker until it answers a real signature — the token can take a moment to activate.</summary>
    private static async Task WaitForWorkerReadyAsync(string baseUrl)
    {
        using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var probeBody = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["encoding"] = "BASE64",
            ["data"] = Convert.ToBase64String("ready-probe"u8.ToArray())
        });

        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                using var content = new StringContent(probeBody, System.Text.Encoding.UTF8, "application/json");
                using var response = await client.PostAsync(
                    new Uri($"/signserver/rest/v1/workers/{WorkerName}/process", UriKind.Relative), content);
                if (response.StatusCode == HttpStatusCode.OK)
                    return;
            }
            catch (HttpRequestException)
            {
                // container still warming up
            }

            await Task.Delay(2000);
        }

        Assert.Fail($"SignServer worker '{WorkerName}' did not become ready in time.");
    }

    private static BundleModel BuildModel(string tempDir, ISignatureProvider provider)
    {
        var payloadPath = Path.Combine(tempDir, "app.msi");
        File.WriteAllText(payloadPath, "e2e-payload-content");

        return new BundleModel
        {
            Name = "SignServerE2EBundle",
            Manufacturer = "TestCo",
            Version = "1.0.0",
            BundleId = Guid.NewGuid(),
            UpgradeCode = Guid.NewGuid(),
            Scope = InstallScope.PerMachine,
            Packages = new List<BundlePackageModel>
            {
                new()
                {
                    Id = "PkgA",
                    SourcePath = payloadPath,
                    Type = BundlePackageType.MsiPackage,
                    DisplayName = "PkgA"
                }
            }.AsReadOnly(),
            Integrity = new IntegrityConfiguration
            {
                SignatureProviders = new[] { provider }
            }
        };
    }

    private static InstallerManifest ExtractManifest(string bundlePath)
    {
        var content = PayloadEmbedder.Extract(bundlePath);
        Assert.True(content.IsSuccess, content.IsFailure ? content.Error.Message : null);
        var manifest = JsonSerializer.Deserialize<InstallerManifest>(content.Value.ManifestJsonBytes!);
        Assert.NotNull(manifest);
        return manifest!;
    }

    /// <summary>
    /// Resolves a container runtime for Testcontainers. Honors an explicit <c>DOCKER_HOST</c>; otherwise
    /// probes Windows named pipes for a Docker or Podman endpoint. Either way, before wiring
    /// <c>DOCKER_HOST</c> for the test it also confirms the daemon can actually run <b>Linux</b> containers
    /// (queries <c>/info</c> for <c>OSType</c>) and disables Ryuk for Podman (rootless Podman cannot run the
    /// privileged reaper). Returns "not available" — so the test skips rather than fails — when nothing is
    /// resolvable, or when a daemon is reachable but only runs Windows containers (e.g. Docker Desktop
    /// switched to "Windows containers" mode: the <c>docker_engine</c> pipe still exists, but a Linux image
    /// pull against it would fail).
    /// </summary>
    /// <summary>
    /// <c>internal</c> (not <c>private</c>) so <see cref="SignServerRotationRevocationE2ETests"/> reuses the
    /// exact same Docker/Podman resolution + Linux-daemon guard instead of re-implementing it.
    /// </summary>
    internal static class ContainerRuntime
    {
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

        public static async Task<(bool Available, string Reason)> TryEnsureConfiguredAsync()
        {
            var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
            var ryukDisabled = false;

            if (string.IsNullOrEmpty(dockerHost))
            {
                if (!OperatingSystem.IsWindows())
                {
                    // On Linux/macOS Testcontainers finds the default socket itself; assume present if the
                    // unix socket exists.
                    if (!File.Exists("/var/run/docker.sock"))
                        return (false, "no /var/run/docker.sock");

                    dockerHost = "unix:///var/run/docker.sock";
                }
                else
                {
                    // A daemon endpoint exists — verify it can actually run Linux containers BEFORE the
                    // caller wires DOCKER_HOST / attempts any image pull. Any probe failure (unreachable
                    // daemon, API error, timeout) is treated as "not available" and never throws.
                    //
                    // A `docker_engine` named pipe can exist and be completely dead (e.g. a Docker Desktop
                    // install that was removed but left the pipe registered, or a stopped service) while a
                    // perfectly good `podman-machine-*` pipe sits right next to it. Probing only the first
                    // candidate found and giving up on failure would report "no runtime" on a machine that
                    // genuinely has one — so every named-pipe candidate is tried, in preference order
                    // (Docker before Podman), and the first one that actually answers as a Linux daemon wins.
                    string[] pipes;
                    try
                    {
                        pipes = Directory.GetFiles(@"\\.\pipe\");
                    }
                    catch (IOException ex)
                    {
                        return (false, $"cannot enumerate named pipes: {ex.Message}");
                    }

                    var candidates = new List<(string Host, bool RyukDisabled)>();
                    if (pipes.Any(p => p.EndsWith("docker_engine", StringComparison.OrdinalIgnoreCase)))
                        candidates.Add(("npipe://./pipe/docker_engine", false));

                    var podman = pipes.FirstOrDefault(p =>
                        p.Contains("podman", StringComparison.OrdinalIgnoreCase) && p.Contains("machine", StringComparison.OrdinalIgnoreCase));
                    if (podman is not null)
                        candidates.Add(($"npipe://./pipe/{Path.GetFileName(podman)}", true));

                    if (candidates.Count == 0)
                        return (false, "no docker_engine or podman-machine named pipe found");

                    var lastReason = "no candidate pipe answered";
                    foreach (var (host, candidateRyukDisabled) in candidates)
                    {
                        var (isLinux, probeReason) = await TryProbeLinuxDaemonAsync(host).ConfigureAwait(false);
                        if (!isLinux)
                        {
                            lastReason = probeReason;
                            continue;
                        }

                        return CommitDaemon(host, candidateRyukDisabled, probeReason);
                    }

                    return (false, lastReason);
                }
            }

            // Explicit DOCKER_HOST (or the non-Windows /var/run/docker.sock path) — only one candidate, so
            // probe it directly.
            var (explicitIsLinux, explicitReason) = await TryProbeLinuxDaemonAsync(dockerHost).ConfigureAwait(false);
            return explicitIsLinux ? CommitDaemon(dockerHost, ryukDisabled, explicitReason) : (false, explicitReason);
        }

        private static (bool Available, string Reason) CommitDaemon(string dockerHost, bool ryukDisabled, string reason)
        {
            Environment.SetEnvironmentVariable("DOCKER_HOST", dockerHost);
            if (ryukDisabled)
            {
                // Rootless Podman cannot run the privileged Ryuk reaper container — disable it and rely on
                // the test's own DisposeAsync teardown.
                Environment.SetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED", "true");
            }

            return (true, reason);
        }

        private static async Task<(bool IsLinux, string Reason)> TryProbeLinuxDaemonAsync(string dockerHost)
        {
            try
            {
                using var config = new DockerClientConfiguration(new Uri(dockerHost), defaultTimeout: ProbeTimeout);
                using var client = config.CreateClient();
                using var cts = new CancellationTokenSource(ProbeTimeout);
                var info = await client.System.GetSystemInfoAsync(cts.Token).ConfigureAwait(false);

                return string.Equals(info.OSType, "linux", StringComparison.OrdinalIgnoreCase)
                    ? (true, $"linux daemon at {dockerHost}")
                    : (false, $"Docker daemon is in Windows-container mode (OSType={info.OSType}); Linux containers required");
            }
            catch (Exception ex)
            {
                return (false, $"container runtime probe failed: {ex.Message}");
            }
        }
    }
}
