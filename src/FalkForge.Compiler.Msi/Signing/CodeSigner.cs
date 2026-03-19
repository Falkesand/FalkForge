using System.Diagnostics;
using System.Runtime.Versioning;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Signing;

[SupportedOSPlatform("windows")]
#pragma warning disable CA1822 // Stateless service; instance method for future extensibility
public sealed class CodeSigner
{
    private static readonly string[] SignToolSearchPaths =
    [
        @"C:\Program Files (x86)\Windows Kits\10\bin",
        @"C:\Program Files\Windows Kits\10\bin",
        @"C:\Program Files (x86)\Microsoft SDKs\ClickOnce\SignTool"
    ];

    public Result<Unit> Sign(string filePath, SigningOptions options)
    {
        if (!File.Exists(filePath))
            return Result<Unit>.Failure(ErrorKind.FileNotFound, $"File not found: {filePath}");

        var signtoolPath = FindSignTool();
        if (signtoolPath is null)
            return Result<Unit>.Failure(ErrorKind.FileNotFound,
                "signtool.exe not found. Install the Windows SDK or specify the path.");

        var args = BuildArguments(filePath, options);

        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = signtoolPath,
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
                return Result<Unit>.Failure(ErrorKind.CompilationError,
                    $"Code signing failed (exit code {process.ExitCode}): {output.Trim()}");
            }

            return Unit.Value;
        }
        catch (Exception ex)
        {
            return Result<Unit>.Failure(ErrorKind.CompilationError, $"Failed to execute signtool.exe: {ex.Message}");
        }
    }

    internal static List<string> BuildArguments(string filePath, SigningOptions options)
    {
        var args = new List<string> { "sign" };

        // Certificate source
        if (!string.IsNullOrEmpty(options.CertificatePath))
        {
            args.Add("/f");
            args.Add(options.CertificatePath);
        }
        else if (!string.IsNullOrEmpty(options.CertificateThumbprint))
        {
            args.Add("/sha1");
            args.Add(options.CertificateThumbprint);
            args.Add("/s");
            args.Add(options.StoreName);
        }

        // Digest algorithm
        args.Add("/fd");
        args.Add(options.DigestAlgorithm);

        // Timestamp
        if (!string.IsNullOrEmpty(options.TimestampUrl))
        {
            args.Add("/tr");
            args.Add(options.TimestampUrl);
            args.Add("/td");
            args.Add(options.DigestAlgorithm);
        }

        // Description
        if (!string.IsNullOrEmpty(options.Description))
        {
            args.Add("/d");
            args.Add(options.Description);
        }

        if (!string.IsNullOrEmpty(options.DescriptionUrl))
        {
            args.Add("/du");
            args.Add(options.DescriptionUrl);
        }

        // File to sign
        args.Add(filePath);

        return args;
    }

    internal static string? FindSignTool()
    {
        foreach (var basePath in SignToolSearchPaths)
        {
            if (!Directory.Exists(basePath))
                continue;

            try
            {
                var signtoolFiles = Directory.GetFiles(basePath, "signtool.exe", SearchOption.AllDirectories);
                if (signtoolFiles.Length > 0)
                {
                    // Prefer x64 version, then newest SDK version
                    var preferred = signtoolFiles
                        .Where(f => f.Contains("x64", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(f => f)
                        .FirstOrDefault();

                    return preferred ?? signtoolFiles.OrderByDescending(f => f).First();
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories we can't access
            }
        }

        return null;
    }
}