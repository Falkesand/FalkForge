using System.Runtime.Versioning;
using FalkForge.Compiler.Msi;
using FalkForge.Engine.Protocol.Integrity;

namespace FalkForge.Cli;

/// <summary>
/// Inspects an MSI database and extracts metadata without modifying the file.
/// </summary>
[SupportedOSPlatform("windows")]
public static class MsiInspector
{
    /// <summary>
    /// Opens an MSI file read-only and extracts summary metadata.
    /// </summary>
    public static Result<MsiInspectionResult> Inspect(string msiPath)
    {
        var dbResult = MsiDatabase.Open(msiPath, readOnly: true);
        if (dbResult.IsFailure)
            return Result<MsiInspectionResult>.Failure(dbResult.Error);

        using var db = dbResult.Value;

        // Read key properties from the Property table
        string? productName = null;
        string? manufacturer = null;
        string? version = null;
        string? productCode = null;

        var propertyResult = db.QueryRows("SELECT `Property`, `Value` FROM `Property`", 2);
        if (propertyResult.IsSuccess)
        {
            foreach (var row in propertyResult.Value)
            {
                switch (row[0])
                {
                    case "ProductName":
                        productName = row[1];
                        break;
                    case "Manufacturer":
                        manufacturer = row[1];
                        break;
                    case "ProductVersion":
                        version = row[1];
                        break;
                    case "ProductCode":
                        productCode = row[1];
                        break;
                }
            }
        }

        // Enumerate tables using _Tables
        var tableNames = new List<string>();
        var tablesResult = db.QueryRows("SELECT `Name` FROM `_Tables`", 1);
        if (tablesResult.IsSuccess)
        {
            foreach (var row in tablesResult.Value)
            {
                if (row[0] is { } name)
                    tableNames.Add(name);
            }
        }

        // Signature presence/format/fingerprint for display only (non-cryptographic — verification
        // is MsiIntegrityVerifier's job). Shares the table-then-sidecar lookup with the verifier so a
        // reproducible-mode MSI (sidecar-only signature, no in-band table) is reported correctly too.
        var signaturePresent = false;
        string? signatureFormatTag = null;
        var signatureFingerprints = new List<string>();

        var located = MsiIntegrityVerifier.LocateEnvelopeJson(db, msiPath);
        if (located is { } envelope)
        {
            signaturePresent = true;
            signatureFormatTag = envelope.FormatTag;
            var parsed = IntegrityEnvelopeCodec.Parse(envelope.Json);
            if (parsed is not null)
            {
                signatureFingerprints = parsed.Signatures
                    .Select(s => s.Fingerprint)
                    .Where(fp => !string.IsNullOrEmpty(fp))
                    .ToList();
            }
        }

        return new MsiInspectionResult
        {
            ProductName = productName,
            Manufacturer = manufacturer,
            Version = version,
            ProductCode = productCode,
            TableNames = tableNames,
            TableCount = tableNames.Count,
            SignaturePresent = signaturePresent,
            SignatureFormatTag = signatureFormatTag,
            SignatureFingerprints = signatureFingerprints
        };
    }

    /// <summary>
    /// Extracts SBOM attestation data from the _FalkForgeIntegrity custom table.
    /// Returns the SBOM string if found, or a failure result if the table or row is missing.
    /// </summary>
    public static Result<string> ExtractSbom(string msiPath)
    {
        var dbResult = MsiDatabase.Open(msiPath, readOnly: true);
        if (dbResult.IsFailure)
            return Result<string>.Failure(dbResult.Error);

        using var db = dbResult.Value;

        var queryResult = db.QueryRows(
            "SELECT `Id`, `Data` FROM `_FalkForgeIntegrity`", 2);

        if (queryResult.IsFailure)
            return Result<string>.Failure(ErrorKind.FileNotFound,
                "No _FalkForgeIntegrity table found in this MSI.");

        foreach (var row in queryResult.Value)
        {
            if (string.Equals(row[0], "SbomAttestation", StringComparison.Ordinal) && row[1] is { } sbomData)
                return sbomData;
        }

        return Result<string>.Failure(ErrorKind.FileNotFound,
            "No SBOM available in this MSI.");
    }
}
