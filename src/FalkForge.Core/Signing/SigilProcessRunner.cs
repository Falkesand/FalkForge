using System.Diagnostics;
using FalkForge.Models;

namespace FalkForge.Signing;

/// <summary>
/// Runs the sigil CLI tool as a child process. Shared by MSI and Bundle compilers.
/// </summary>
internal static class SigilProcessRunner
{
    internal static Result<string> Run(List<string> args, ErrorKind failureKind)
    {
        if (!SigilDetector.IsAvailable())
            return Result<string>.Failure(ErrorKind.FileNotFound,
                "sigil CLI tool not found. Install sigil or ensure it is on the PATH.");

        try
        {
            using var process = new Process();
#pragma warning disable S4036 // PATH lookup is the documented contract: sigil is a user-installed build-time CLI tool (like git/dotnet)
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "sigil",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
#pragma warning restore S4036

            foreach (var arg in args)
                process.StartInfo.ArgumentList.Add(arg);

            process.Start();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(TimeSpan.FromMinutes(2));

            if (process.ExitCode != 0)
            {
                var output = string.IsNullOrEmpty(stderr) ? stdout : stderr;
                return Result<string>.Failure(failureKind,
                    $"sigil failed (exit code {process.ExitCode}): {output.Trim()}");
            }

            return Result<string>.Success(stdout.Trim());
        }
        catch (Exception ex)
        {
            return Result<string>.Failure(failureKind,
                $"Failed to execute sigil: {ex.Message}");
        }
    }

    internal static void AppendKeyArgs(List<string> args, IntegrityConfiguration? config)
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
