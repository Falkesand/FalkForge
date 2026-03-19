using System.Diagnostics;
using FalkForge.Models;

namespace FalkForge.Compiler.Bundle.Compilation;

internal sealed class BundleSigilSigner
{
    internal Result<string> RunSignManifest(
        string payloadDir,
        string outputPath,
        IntegrityConfiguration? config)
    {
        var args = new List<string> { "sign-manifest", payloadDir, "--output", outputPath };
        AppendKeyArgs(args, config);
        return RunSigil(args);
    }

    internal Result<string> RunAttest(
        string artifactPath,
        string sbomPath,
        SbomFormat format,
        string outputPath,
        IntegrityConfiguration? config)
    {
        var formatString = format switch
        {
            SbomFormat.CycloneDx => "cyclonedx",
            _ => "spdx"
        };

        var args = new List<string>
        {
            "attest",
            artifactPath,
            "--predicate", sbomPath,
            "--type", formatString,
            "--output", outputPath
        };

        AppendKeyArgs(args, config);
        return RunSigil(args);
    }

    private static Result<string> RunSigil(List<string> args)
    {
        if (!BundleSigilDetector.IsAvailable())
            return Result<string>.Failure(ErrorKind.FileNotFound,
                "sigil CLI tool not found. Install sigil or ensure it is on the PATH.");

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "sigil",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (var arg in args)
                process.StartInfo.ArgumentList.Add(arg);

            process.Start();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(TimeSpan.FromMinutes(2));

            if (process.ExitCode != 0)
            {
                var output = string.IsNullOrEmpty(stderr) ? stdout : stderr;
                return Result<string>.Failure(ErrorKind.BundleError,
                    $"sigil failed (exit code {process.ExitCode}): {output.Trim()}");
            }

            return Result<string>.Success(stdout.Trim());
        }
        catch (Exception ex)
        {
            return Result<string>.Failure(ErrorKind.BundleError,
                $"Failed to execute sigil: {ex.Message}");
        }
    }

    private static void AppendKeyArgs(List<string> args, IntegrityConfiguration? config)
    {
        if (config is null)
            return;

        if (!string.IsNullOrEmpty(config.SigningKeyPath))
        {
            args.Add("--key");
            args.Add(config.SigningKeyPath);
        }
        else if (!string.IsNullOrEmpty(config.CertStoreThumbprint))
        {
            args.Add("--cert-store");
            args.Add(config.CertStoreThumbprint);

            if (!string.IsNullOrEmpty(config.StoreLocation))
            {
                args.Add("--store-location");
                args.Add(config.StoreLocation);
            }
        }
        else if (!string.IsNullOrEmpty(config.VaultProvider))
        {
            args.Add("--vault");
            args.Add(config.VaultProvider);

            if (!string.IsNullOrEmpty(config.VaultKeyRef))
            {
                args.Add("--vault-key");
                args.Add(config.VaultKeyRef);
            }
        }
    }
}
