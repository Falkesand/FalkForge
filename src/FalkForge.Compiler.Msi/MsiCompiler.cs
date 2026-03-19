using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Signing;
using FalkForge.Compiler.Msi.Tables;
using FalkForge.Compiler.Msi.Validation;
using FalkForge.Models;
using FalkForge.Platform;
using FalkForge.Platform.Windows;
using FalkForge.Validation;

namespace FalkForge.Compiler.Msi;

[SupportedOSPlatform("windows")]
public sealed class MsiCompiler : ICompiler
{
    private readonly IFileSystem _fileSystem;

    public MsiCompiler() : this(new WindowsFileSystem())
    {
    }

    public MsiCompiler(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public Result<string> Compile(PackageModel package, string outputPath)
    {
        // Step 1: Validate
        var validation = ModelValidator.Validate(package);
        if (!validation.IsValid)
        {
            var errors = string.Join("; ", validation.Errors.Select(e => $"{e.Code}: {e.Message}"));
            return Result<string>.Failure(ErrorKind.Validation, $"Package validation failed: {errors}");
        }

        // Step 2: Resolve components
        var resolver = new ComponentResolver(_fileSystem);
        var resolveResult = resolver.Resolve(package);
        if (resolveResult.IsFailure)
            return Result<string>.Failure(resolveResult.Error);

        var resolved = resolveResult.Value;

        // Step 3: Determine output file name
        var msiFileName = $"{FileNameSanitizer.Sanitize(package.Name)}-{package.Version.ToString(3)}.msi";
        var msiPath = Path.Combine(outputPath, msiFileName);

        // Ensure output directory exists
        var outputDir = Path.GetDirectoryName(msiPath);
        if (outputDir is not null && !Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        // Remove existing file
        if (File.Exists(msiPath))
            File.Delete(msiPath);

        // Step 4: Create MSI database
        var dbResult = MsiDatabase.Create(msiPath);
        if (dbResult.IsFailure)
            return Result<string>.Failure(dbResult.Error);

        // Steps 5-7: all database work inside a nested block
        {
            using var database = dbResult.Value;

            // Step 5: Emit tables
            var tableEmitter = new TableEmitter(database);
            var emitResult = tableEmitter.EmitAllTables(resolved);
            if (emitResult.IsFailure)
                return Result<string>.Failure(emitResult.Error);

            // Step 5.5: Build cabinet and embed into MSI
            if (resolved.Files.Count > 0)
            {
                var tempCabPath = Path.Combine(Path.GetTempPath(), $"FalkForge_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempCabPath);
                try
                {
                    using var cabBuilder = new CabinetBuilder(package.ReproducibleOptions?.Timestamp);
                    var cabResult = cabBuilder.BuildCabinet(resolved.Files, tempCabPath, package.Compression);
                    if (cabResult.IsFailure)
                        return Result<string>.Failure(cabResult.Error);

                    var embedResult = EmbedCabinet(database, cabResult.Value);
                    if (embedResult.IsFailure)
                        return Result<string>.Failure(embedResult.Error);
                }
                finally
                {
                    if (Directory.Exists(tempCabPath))
                        Directory.Delete(tempCabPath, true);
                }
            }

            // Step 6: Set summary information
            var summaryResult = database.SetSummaryInfo(summary =>
            {
                summary
                    .Title("Installation Database")
                    .Subject(package.Name)
                    .Author(package.Manufacturer)
                    .Keywords("Installer")
                    .Comments(package.Description ??
                              $"This installer database contains the logic and data required to install {package.Name}.")
                    .Template(GetPlatformTemplate(package.Architecture))
                    .RevisionNumber(package.ProductCode.ToString("B").ToUpperInvariant())
                    .CreatingApplication("FalkForge")
                    .WordCount(2) // Compressed, LongFileNames
                    .PageCount(200) // Minimum installer version
                    .Security(2) // Read-only recommended
                    .Codepage(1252);
            });
            if (summaryResult.IsFailure)
                return Result<string>.Failure(summaryResult.Error);

            // Step 7: Commit
            var commitResult = database.Commit();
            if (commitResult.IsFailure)
                return Result<string>.Failure(commitResult.Error);
        } // database disposed here — file handle released

        // Step 7.5: Patch SummaryInfo timestamps in the output file when building reproducibly.
        // Windows MsiSummaryInfoPersist always updates PID_LASTSAVE_DTM to the current system
        // time on every call, making it impossible to pin the timestamp via the MSI API alone.
        // The patcher walks the OLE2 compound document and overwrites the FILETIME values in place.
        if (package.ReproducibleOptions is { } reproducibleOpts)
        {
            var patchResult = SummaryInfoPatcher.PatchTimestamps(msiPath, reproducibleOpts.Timestamp);
            if (patchResult.IsFailure)
                return Result<string>.Failure(patchResult.Error);
        }

        // Step 8: Code signing (if configured)
        if (package.Signing is not null)
        {
            var signer = new CodeSigner();
            var signResult = signer.Sign(msiPath, package.Signing);
            if (signResult.IsFailure)
                return Result<string>.Failure(signResult.Error);
        }

        // Step 8.5: Integrity signing (opportunistic -- only if Sigil is on PATH)
        if (!IsIntegritySigningDisabled() && SigilDetector.IsAvailable() && package.Integrity is not null)
        {
            var integrityResult = IntegritySigner.SignAndEmbed(msiPath, package, resolved.Files);
            if (integrityResult.IsFailure)
                return Result<string>.Failure(integrityResult.Error);
        }

        // Step 9: ICE validation
        var iceConfig = package.IceConfiguration ?? new IceConfiguration();
        if (iceConfig.Enabled && package.ReproducibleOptions is null)
        {
            var iceValidator = new IceValidator();
            var iceResult = iceValidator.Validate(msiPath, iceConfig);
            if (iceResult.IsFailure)
            {
                // ICE validation infrastructure failure is non-fatal
            }
            else
            {
                if (iceConfig.ReportPath is not null)
                    IceReportExporter.Export(iceResult.Value, iceConfig.ReportPath);

                if (iceResult.Value.Errors.Count > 0 || iceResult.Value.Failures.Count > 0)
                {
                    var iceErrors = string.Join("; ", iceResult.Value.Messages
                        .Where(m => m.Severity is IceMessageSeverity.Error or IceMessageSeverity.Failure)
                        .Select(m => $"{m.IceName}: {m.Description}"));
                    return Result<string>.Failure(ErrorKind.Validation, $"ICE validation failed: {iceErrors}");
                }
            }
        }

        // Step 10: SBOM sidecar (opt-in via SbomOptions or FALKFORGE_GENERATE_SBOM env var)
        // SBOM failure is non-fatal — log and continue.
        var sbomResult = SbomHelper.WriteSbomSidecar(package, resolved.Files, msiPath);
        _ = sbomResult; // warning suppression; caller may inspect via tooling

        return msiPath;
    }

    private static bool IsIntegritySigningDisabled()
    {
        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FALKFORGE_NO_SIGN"));
    }

    private static string GetPlatformTemplate(ProcessorArchitecture architecture)
    {
        return architecture switch
        {
            ProcessorArchitecture.X86 => "Intel;1033",
            ProcessorArchitecture.X64 => "x64;1033",
            ProcessorArchitecture.Arm64 => "Arm64;1033",
            _ => "x64;1033"
        };
    }

    private static Result<Unit> EmbedCabinet(MsiDatabase database, string cabPath)
    {
        // The _Streams table is a special MSI table for embedded streams.
        // Ignore errors from CREATE since the table may already exist.
        database.Execute(
            "CREATE TABLE `_Streams` (`Name` CHAR(72) NOT NULL, `Data` OBJECT NOT NULL PRIMARY KEY `Name`)");

        return database.InsertRow(
            "SELECT `Name`, `Data` FROM `_Streams`",
            record => record
                .SetString(1, "Data.cab")
                .SetStream(2, cabPath));
    }
}