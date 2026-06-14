using System.Runtime.Versioning;
using System.Text;
using FalkForge.Compiler.Bundle;
using FalkForge.Engine.Protocol.Bundle;
using FalkForge.Models;

namespace FalkForge.Decompiler;

/// <summary>
/// Generates a compilable FalkForge installer project from an existing installer binary.
///
/// Supported:
///   .msi / .msm — full MSI decompilation via <see cref="MsiDecompiler"/> (Windows-only).
///   .exe        — bundle decompilation: a native FalkForge bundle (FALKBUNDLE) via
///                 <see cref="BundleDecompiler"/> (cross-platform), otherwise a WiX Burn
///                 bundle via <see cref="WixBundleDecompiler"/> (Windows-only).
/// </summary>
public sealed class MigrationProjectGenerator
{
    private readonly MsiDecompiler? _msiDecompiler;
    private readonly BundleDecompiler? _bundleDecompiler;
    private readonly WixBundleDecompiler? _wixDecompiler;

    /// <summary>
    /// Creates a generator that opens installer files directly from disk (production path).
    /// </summary>
    public MigrationProjectGenerator()
    {
        // All decompilers are null; the production path creates them on demand.
    }

    /// <summary>
    /// Creates a generator with an injected <see cref="MsiDecompiler"/> — primarily for testing
    /// so that a <see cref="MockMsiTableAccess"/> can be supplied without touching the filesystem.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public MigrationProjectGenerator(MsiDecompiler msiDecompiler)
    {
        _msiDecompiler = msiDecompiler;
    }

    /// <summary>
    /// Creates a generator with an injected native <see cref="BundleDecompiler"/> — for testing
    /// the FALKBUNDLE branch with a <see cref="IBundleAccess"/> mock (no real bundle on disk).
    /// </summary>
    public MigrationProjectGenerator(BundleDecompiler bundleDecompiler)
    {
        _bundleDecompiler = bundleDecompiler;
    }

    /// <summary>
    /// Creates a generator with injected native and WiX Burn decompilers — for testing the
    /// WiX fallback branch (the native decompiler is configured to fail, so routing falls
    /// through to WiX).
    /// </summary>
    public MigrationProjectGenerator(BundleDecompiler bundleDecompiler, WixBundleDecompiler wixDecompiler)
    {
        _bundleDecompiler = bundleDecompiler;
        _wixDecompiler = wixDecompiler;
    }

    /// <summary>
    /// Generates a migration project from <paramref name="inputPath"/>.
    /// Routes to the appropriate decompiler based on file extension.
    /// </summary>
    public Result<MigrationResult> Generate(string inputPath, MigrationOptions options)
    {
        var ext = Path.GetExtension(inputPath).ToLowerInvariant();

        if (ext is ".msi" or ".msm")
        {
            if (!OperatingSystem.IsWindows())
                return Result<MigrationResult>.Failure(
                    ErrorKind.Validation,
                    "MSI migration requires Windows.");

            return GenerateFromMsi(inputPath, options);
        }

        return ext switch
        {
            ".exe" => GenerateFromBundle(inputPath, options),
            _      => Result<MigrationResult>.Failure(
                          ErrorKind.Validation,
                          $"Unrecognised installer extension '{ext}'. Supported: .msi, .msm, .exe.")
        };
    }

    // ── MSI branch ───────────────────────────────────────────────────────────

    [SupportedOSPlatform("windows")]
    private Result<MigrationResult> GenerateFromMsi(string inputPath, MigrationOptions options)
    {
        // Reuse injected decompiler (tests) or create a fresh one (production).
        var decompiler = _msiDecompiler ?? new MsiDecompiler();

        // Decompile to the model first so the report can honestly enumerate which
        // model features are still not emitted; emit C# from the same model.
        var modelResult = decompiler.Decompile(inputPath);
        if (modelResult.IsFailure)
            return Result<MigrationResult>.Failure(modelResult.Error);

        var model = modelResult.Value;

        // Emit via the Result-returning path so an unsupported KnownFolder root token
        // surfaces as a Failure (preserving the Result<MigrationResult> contract and
        // avoiding a stack-trace leak) instead of escaping as an exception.
        var emitResult = new CSharpEmitter().TryEmit(model);
        if (emitResult.IsFailure)
            return Result<MigrationResult>.Failure(emitResult.Error);
        var emittedFragment = emitResult.Value;

        var programCs   = BuildProgramCs(emittedFragment);
        var csproj      = BuildCsproj(options);
        var report      = BuildReport(inputPath, options, model);

        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Program.cs"]                            = programCs,
            [$"{options.ProjectName}.csproj"]         = csproj,
            ["MIGRATION-REPORT.md"]                   = report,
        };

        // Extract payload bytes only on the production path (a real MSI file on disk).
        // The injected-mock decompiler has no cabinet access, so its Payloads stay empty.
        IReadOnlyDictionary<string, byte[]> payloads =
            new Dictionary<string, byte[]>(StringComparer.Ordinal);

        if (_msiDecompiler is null && File.Exists(inputPath))
        {
            var payloadResult = MsiPayloadExtractor.Extract(inputPath);
            if (payloadResult.IsFailure)
                return Result<MigrationResult>.Failure(payloadResult.Error);
            payloads = payloadResult.Value;
        }

        return Result<MigrationResult>.Success(
            new MigrationResult(files, [], payloads));
    }

    // ── bundle branch ─────────────────────────────────────────────────────────

    private Result<MigrationResult> GenerateFromBundle(string inputPath, MigrationOptions options)
    {
        // Mirror DecompileCommand routing: try the native FALKBUNDLE decompiler first
        // (cross-platform); if it fails, fall back to WiX Burn (Windows-only).
        var native = _bundleDecompiler ?? new BundleDecompiler();
        var nativeResult = native.Decompile(inputPath);
        if (nativeResult.IsSuccess)
            return GenerateNativeBundle(inputPath, options, nativeResult.Value);

        return GenerateWixBundle(inputPath, options, nativeResult.Error);
    }

    private Result<MigrationResult> GenerateNativeBundle(string inputPath, MigrationOptions options, BundleModel model)
    {
        // ONE map drives both the emitted chain paths and the extracted-bytes keys.
        // Build it from the SAME collection the emitter iterates (the chain's package
        // instances), not model.Packages — the two may differ, and a chain package id
        // absent from the map would silently fall back to a path the bytes were never
        // keyed under. Keying off the chain guarantees alignment by construction.
        var chainPackages = model.Chain
            .OfType<PackageChainItem>()
            .Select(item => item.Package)
            .ToList();
        var payloadKeys = BundlePayloadPath.BuildMap(chainPackages);

        var emitted = BundleCSharpEmitter.Emit(
            model,
            preamble: null,
            unmappedFeatures: null,
            packagePathResolver: pkg => Resolve(payloadKeys, pkg));

        var programCs = BuildBundleProgramCs(emitted);
        var csproj = BuildBundleCsproj(options);
        var report = BuildBundleReport(inputPath, options, detectedType: "FalkForge bundle", unmapped: []);

        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Program.cs"] = programCs,
            [$"{options.ProjectName}.csproj"] = csproj,
            ["MIGRATION-REPORT.md"] = report,
        };

        var payloads = ExtractBundlePayloads(inputPath, model, payloadKeys);

        return Result<MigrationResult>.Success(new MigrationResult(files, [], payloads));
    }

    private Result<MigrationResult> GenerateWixBundle(string inputPath, MigrationOptions options, Error nativeError)
    {
        if (!OperatingSystem.IsWindows())
            return Result<MigrationResult>.Failure(
                ErrorKind.Validation,
                $"Bundle (.exe) migration requires Windows for WiX Burn bundles. " +
                $"It is not a native FalkForge bundle ({nativeError.Message}).");

        var wix = _wixDecompiler ?? new WixBundleDecompiler();
        var wixResult = wix.DecompileWithUnmapped(inputPath);
        if (wixResult.IsFailure)
            return Result<MigrationResult>.Failure(wixResult.Error);

        var (model, unmapped) = wixResult.Value;

        // WiX bundles reference their payloads by external SourceFile paths; there are no
        // FalkForge-embedded payload bytes to extract here, so emit the original paths and
        // leave Payloads empty (the report's unmapped section flags what needs manual work).
        var emitted = BundleCSharpEmitter.Emit(model, preamble: null, unmappedFeatures: unmapped);

        var programCs = BuildBundleProgramCs(emitted);
        var csproj = BuildBundleCsproj(options);
        var report = BuildBundleReport(inputPath, options, detectedType: "WiX Burn bundle", unmapped: unmapped);

        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Program.cs"] = programCs,
            [$"{options.ProjectName}.csproj"] = csproj,
            ["MIGRATION-REPORT.md"] = report,
        };

        IReadOnlyDictionary<string, byte[]> payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        return Result<MigrationResult>.Success(new MigrationResult(files, unmapped, payloads));
    }

    private static string Resolve(IReadOnlyDictionary<string, string> map, BundlePackageModel pkg) =>
        map.TryGetValue(pkg.Id, out var key) ? key : BundlePayloadPath.For(pkg.SourcePath);

    /// <summary>
    /// Extracts the bundle's embedded chain payload bytes keyed by the SAME payload path
    /// that the emitted chain references (so the generated code and the written bytes align
    /// by construction). Returns an empty map when the bundle file cannot be read (e.g. the
    /// injected-mock test path, which has no real bundle on disk).
    /// </summary>
    private IReadOnlyDictionary<string, byte[]> ExtractBundlePayloads(
        string inputPath, BundleModel model, IReadOnlyDictionary<string, string> payloadKeys)
    {
        var payloads = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        // Only attempt extraction on the production path (a real bundle EXE on disk).
        if (_bundleDecompiler is not null || !File.Exists(inputPath))
            return payloads;

        var contentResult = BundleReader.Extract(inputPath);
        if (contentResult.IsFailure)
            return payloads;

        var packageById = model.Packages.ToDictionary(p => p.Id, StringComparer.Ordinal);

        foreach (var entry in contentResult.Value.TocEntries)
        {
            if (!packageById.ContainsKey(entry.PackageId) ||
                !payloadKeys.TryGetValue(entry.PackageId, out var key))
                continue;

            var payloadResult = BundleReader.ExtractPayload(inputPath, entry);
            if (payloadResult.IsSuccess)
                payloads[key] = payloadResult.Value;
        }

        return payloads;
    }

    /// <summary>
    /// Wraps the emitter's illustrative <c>Installer.BuildBundle(b =&gt; { ... });</c> output
    /// into the runnable entry point:
    /// <code>
    /// var b = new BundleBuilder();
    /// ... b.X(...) ...
    /// var bundle = b.Build();
    /// return Installer.BuildBundle(args, outputPath =&gt; new BundleCompiler().Compile(bundle, outputPath));
    /// </code>
    /// The emitter body already uses statement-form calls on <c>b</c> (and <c>c</c> inside
    /// <c>b.Chain</c>), which are valid against a real <see cref="BundleBuilder"/> instance.
    /// </summary>
    private static string BuildBundleProgramCs(string emittedFragment)
    {
        const string openMarker = "Installer.BuildBundle(b =>";
        const string compilationUsing = "using FalkForge.Compiler.Bundle.Compilation;";

        var lines = emittedFragment.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var sb = new StringBuilder(emittedFragment.Length + 256);

        var compilationUsingInjected = false;
        var openMarkerSeen = false;
        var openBraceSkipped = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // Inject the Compilation using right after the Builders using line.
            if (!compilationUsingInjected &&
                line.Contains("using FalkForge.Compiler.Bundle.Builders;", StringComparison.Ordinal))
            {
                sb.AppendLine(line);
                sb.AppendLine(compilationUsing);
                compilationUsingInjected = true;
                continue;
            }

            // Replace the "Installer.BuildBundle(b =>" header with the builder construction.
            if (!openMarkerSeen && line.Contains(openMarker, StringComparison.Ordinal))
            {
                sb.AppendLine("var b = new BundleBuilder();");
                openMarkerSeen = true;
                continue;
            }

            // Skip the single "{" line that opened the lambda body.
            if (openMarkerSeen && !openBraceSkipped && line.Trim() == "{")
            {
                openBraceSkipped = true;
                continue;
            }

            // Replace the closing "});" of the OUTER lambda with the build + runnable entry
            // point. Inner lambdas (b.Feature, b.Chain, per-package configurators) also close
            // with "});" but are indented; the emitter writes the outer close at column 0, so
            // match on the exact zero-indent "});" to avoid swapping a nested block's close
            // (which would splice the entry point into the middle of the builder calls).
            if (openMarkerSeen && line == "});")
            {
                sb.AppendLine("var bundle = b.Build();");
                sb.AppendLine("return Installer.BuildBundle(args, outputPath => new BundleCompiler().Compile(bundle, outputPath));");
                openMarkerSeen = false;
                continue;
            }

            sb.AppendLine(line);
        }

        return sb.ToString();
    }

    private static string BuildBundleCsproj(MigrationOptions options)
    {
        var src = options.FalkForgeSourcePath.Replace('\\', '/');

        return $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0-windows</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="{src}/FalkForge.Core/FalkForge.Core.csproj" />
                    <ProjectReference Include="{src}/FalkForge.Compiler.Bundle/FalkForge.Compiler.Bundle.csproj" />
                  </ItemGroup>
                  <ItemGroup>
                    <None Include="payload/**" CopyToOutputDirectory="PreserveNewest" />
                  </ItemGroup>
                </Project>
                """;
    }

    private static string BuildBundleReport(
        string inputPath,
        MigrationOptions options,
        string detectedType,
        IReadOnlyList<WixUnmappedFeature> unmapped)
    {
        var fileName = Path.GetFileName(inputPath);

        var sb = new StringBuilder();
        sb.AppendLine("# Migration Report");
        sb.AppendLine();
        sb.AppendLine("| Field | Value |");
        sb.AppendLine("|-------|-------|");
        sb.AppendLine($"| Source file | `{fileName}` |");
        sb.AppendLine($"| Detected type | {detectedType} |");
        sb.AppendLine($"| Project name | {options.ProjectName} |");
        sb.AppendLine();
        sb.AppendLine("## Notes");
        sb.AppendLine();
        sb.AppendLine(
            "Bundle decompilation maps the chain (packages and rollback boundaries), bundle "
            + "identity, variables, features, related bundles, containers, and UI configuration. "
            + "Native FalkForge bundles also have their embedded chain payloads extracted into the "
            + "`payload/` directory; each chained package references its payload-relative path.");
        sb.AppendLine();
        sb.AppendLine("## Not yet migrated");
        sb.AppendLine();
        sb.AppendLine(
            "UI assets (logo, theme, watermark, banner), container download URLs, and custom UI "
            + "project paths are not preserved. Re-add them manually in `Program.cs` if required.");

        if (unmapped.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Unmapped WiX Burn features");
            sb.AppendLine();
            sb.AppendLine(
                "The following WiX Burn features have no FalkForge equivalent and were NOT migrated. "
                + "Re-implement them manually:");
            sb.AppendLine();
            foreach (var feature in unmapped)
            {
                sb.Append("- **").Append(feature.Category).Append("**: ").AppendLine(feature.Description);
                if (!string.IsNullOrEmpty(feature.OriginalXml) && feature.OriginalXml.Length <= 200)
                    sb.Append("  - `").Append(feature.OriginalXml).AppendLine("`");
            }
        }

        return sb.ToString().TrimEnd();
    }

    // ── MSI program.cs builder ─────────────────────────────────────────────────

    private static string BuildProgramCs(string emittedFragment)
    {
        // The emitter already emits:
        //   using FalkForge;
        //   using FalkForge.Builders;
        //   using FalkForge.Models;
        //   ...
        //   var model = builder.Build();
        //
        // We need to inject "using FalkForge.Compiler.Msi;" (for MsiCompiler)
        // and append the Installer.Build call that drives the actual MSI compilation.
        //
        // Strategy: inject the missing using right after the existing using block
        // (before the first blank line that separates usings from statements).

        const string msiUsing = "using FalkForge.Compiler.Msi;";
        const string entryPoint = "return Installer.Build(args, model, new MsiCompiler());";

        var sb = new StringBuilder(emittedFragment.Length + 128);

        if (!emittedFragment.Contains(msiUsing, StringComparison.Ordinal))
        {
            // Insert after the last "using ..." line in the block.
            // Find the index of the first blank line that ends the using block.
            var lines = emittedFragment.Split('\n');
            var insertedUsing = false;
            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd('\r');
                sb.AppendLine(line);
                if (!insertedUsing && string.IsNullOrWhiteSpace(line))
                {
                    // This blank line separates the using block from the body — insert before it.
                    // Back up: remove the blank line we just added, add using, re-add blank line.
                    // Actually: we already wrote it. Insert the using BEFORE the blank line.
                    // Redo: clear last blank line, write using, write blank line.
                    // Simpler approach: detect last using line, inject immediately after.
                    insertedUsing = true;
                }
            }

            // Simpler and more robust: rebuild with injection.
            sb.Clear();
            var usingBlockDone = false;
            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd('\r');
                if (!usingBlockDone && string.IsNullOrWhiteSpace(line) &&
                    sb.ToString().Contains("using ", StringComparison.Ordinal))
                {
                    // End of using block — inject our using before the blank separator.
                    sb.AppendLine(msiUsing);
                    usingBlockDone = true;
                }
                sb.AppendLine(line);
            }
        }
        else
        {
            sb.Append(emittedFragment);
        }

        // Append entry point (Installer.Build) after builder.Build().
        sb.AppendLine(entryPoint);

        return sb.ToString();
    }

    private static string BuildCsproj(MigrationOptions options)
    {
        // Forward slashes in XML paths — consistent cross-platform and readable.
        var src = options.FalkForgeSourcePath.Replace('\\', '/');

        return $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0-windows</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="{src}/FalkForge.Core/FalkForge.Core.csproj" />
                    <ProjectReference Include="{src}/FalkForge.Compiler.Msi/FalkForge.Compiler.Msi.csproj" />
                  </ItemGroup>
                  <ItemGroup>
                    <None Include="payload/**" CopyToOutputDirectory="PreserveNewest" />
                  </ItemGroup>
                </Project>
                """;
    }

    private static string BuildReport(string inputPath, MigrationOptions options, PackageModel model)
    {
        var fileName = Path.GetFileName(inputPath);
        var ext      = Path.GetExtension(inputPath).ToUpperInvariant().TrimStart('.');

        return $"""
                # Migration Report

                | Field | Value |
                |-------|-------|
                | Source file | `{fileName}` |
                | Detected type | {ext} |
                | Project name | {options.ProjectName} |
                | Mapping coverage | MSI decompilation maps the supported tables (see below). |

                ## Notes

                MSI decompilation covers: package metadata, features, files (payload entries are
                emitted as `files.Add("payload/...")` calls and the payload bytes are written to the
                `payload/` directory), registry entries, services, shortcuts, and properties.

                No unmapped WiX features (this is an MSI source, not a WiX bundle).

                {BuildNotMigratedSection(model)}
                """;
    }

    /// <summary>
    /// Honestly lists model feature categories that are present in the decompiled
    /// <paramref name="model"/> but are NOT yet emitted by <see cref="CSharpEmitter"/>,
    /// so the migrator knows exactly what to re-add by hand. Returns a positive
    /// "all mapped" note when nothing is dropped.
    /// </summary>
    internal static string BuildNotMigratedSection(PackageModel model)
    {
        var dropped = new List<string>();

        if (model.EnvironmentVariables.Count > 0) dropped.Add("environment variables");
        if (model.CustomActions.Count > 0)        dropped.Add("custom actions");
        if (model.CustomTables.Count > 0)          dropped.Add("custom tables");
        if (model.ExecuteSequenceActions.Count > 0 || model.UISequenceActions.Count > 0)
            dropped.Add("sequence scheduling");
        if (model.IniFiles.Count > 0)              dropped.Add("INI files");
        if (model.FileAssociations.Count > 0)      dropped.Add("file associations");
        if (model.Fonts.Count > 0)                 dropped.Add("fonts");
        if (model.Permissions.Count > 0)           dropped.Add("permissions");

        if (dropped.Count == 0)
            return "## Not yet migrated\n\nAll present features were mapped.";

        var sb = new StringBuilder("## Not yet migrated\n\n");
        sb.AppendLine(
            "The following features are present in the source installer but are NOT yet emitted "
            + "by the migrator. Re-add them manually in `Program.cs`:");
        sb.AppendLine();
        foreach (var item in dropped)
            sb.Append("- ").AppendLine(item);

        return sb.ToString().TrimEnd();
    }
}
