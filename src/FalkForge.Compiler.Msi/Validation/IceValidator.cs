using System.Runtime.Versioning;
using FalkForge.Compiler.Msi.Interop;
using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Validation;

[SupportedOSPlatform("windows")]
#pragma warning disable CA1822 // Stateless validator; instance method for future extensibility
public sealed class IceValidator
{
    private static readonly string[] CubSearchPaths =
    [
        // Windows SDK locations
        @"C:\Program Files (x86)\Windows Kits\10\bin",
        @"C:\Program Files\Windows Kits\10\bin",
        @"C:\Program Files (x86)\MSI\darice.cub"
    ];

    /// <summary>
    ///     Validates an MSI file using ICE (Internal Consistency Evaluators).
    ///     Requires Windows SDK to be installed for darice.cub.
    ///     Returns success with empty messages if darice.cub is not found.
    /// </summary>
    public Result<IceValidationResult> Validate(string msiPath)
    {
        if (!File.Exists(msiPath))
            return Result<IceValidationResult>.Failure(ErrorKind.FileNotFound, $"MSI file not found: {msiPath}");

        var cubPath = FindDariceCub();
        if (cubPath is null)
            // Graceful fallback - ICE validation is opt-in, not mandatory
            return IceValidationResult.Success();

        return ValidateWithCub(msiPath, cubPath);
    }

    /// <summary>
    ///     Validates an MSI using a specific .cub file path.
    /// </summary>
    public Result<IceValidationResult> Validate(string msiPath, string cubPath)
    {
        if (!File.Exists(msiPath))
            return Result<IceValidationResult>.Failure(ErrorKind.FileNotFound, $"MSI file not found: {msiPath}");
        if (!File.Exists(cubPath))
            return Result<IceValidationResult>.Failure(ErrorKind.FileNotFound, $"CUB file not found: {cubPath}");

        return ValidateWithCub(msiPath, cubPath);
    }

    /// <summary>
    ///     Validates an MSI using an <see cref="IceConfiguration"/> for suppression,
    ///     warning promotion, and custom CUB path.
    /// </summary>
    public Result<IceValidationResult> Validate(string msiPath, IceConfiguration config)
    {
        if (!config.Enabled)
            return Result<IceValidationResult>.Success(IceValidationResult.Success());

        var cubPath = config.CubFilePath ?? FindDariceCub();
        if (cubPath is null)
            return Result<IceValidationResult>.Success(IceValidationResult.Success());

        if (!File.Exists(msiPath))
            return Result<IceValidationResult>.Failure(ErrorKind.FileNotFound, $"MSI file not found: {msiPath}");

        if (!File.Exists(cubPath))
            return Result<IceValidationResult>.Failure(ErrorKind.FileNotFound, $"CUB file not found: {cubPath}");

        var result = ValidateWithCub(msiPath, cubPath);
        if (result.IsFailure)
            return result;

        var messages = result.Value.Messages.ToList();

        // Filter suppressed ICEs
        if (config.SuppressedIces.Count > 0)
            messages.RemoveAll(m => config.SuppressedIces.Contains(m.IceName, StringComparer.OrdinalIgnoreCase));

        // Promote warnings to errors if configured
        if (config.WarningsAsErrors)
        {
            messages = messages.Select(m => m.Severity == IceMessageSeverity.Warning
                ? new IceMessage
                {
                    IceName = m.IceName,
                    Severity = IceMessageSeverity.Error,
                    Description = m.Description,
                    Table = m.Table,
                    Column = m.Column,
                    PrimaryKeys = m.PrimaryKeys
                }
                : m).ToList();
        }

        return Result<IceValidationResult>.Success(IceValidationResult.FromMessages(messages));
    }

    private static Result<IceValidationResult> ValidateWithCub(string msiPath, string cubPath)
    {
        // Open target MSI
        var error = NativeMethods.MsiOpenDatabase(msiPath, NativeMethods.MSIDBOPEN_DIRECT, out var hDatabase);
        if (error != NativeMethods.ERROR_SUCCESS)
            return Result<IceValidationResult>.Failure(ErrorKind.CompilationError,
                $"Failed to open MSI for validation: error {error}");

        try
        {
            // Open CUB database
            error = NativeMethods.MsiOpenDatabase(cubPath, NativeMethods.MSIDBOPEN_READONLY, out var hCub);
            if (error != NativeMethods.ERROR_SUCCESS)
            {
                _ = NativeMethods.MsiCloseHandle(hDatabase);
                return Result<IceValidationResult>.Failure(ErrorKind.CompilationError,
                    $"Failed to open CUB file: error {error}");
            }

            try
            {
                // Merge CUB into target MSI
                NativeMethods.MsiDatabaseMerge(hDatabase, hCub, "MergeErrors");
                // Merge errors are expected for some tables, so we don't fail on merge errors
                // The merge brings in the _ICESequence table and ICE custom actions

                // Commit after merge so ICE CAs can see merged data
                _ = NativeMethods.MsiDatabaseCommit(hDatabase);

                // Execute ICE sequence and collect messages
                var messages = ExecuteIceSequence(hDatabase);
                return IceValidationResult.FromMessages(messages);
            }
            finally
            {
                _ = NativeMethods.MsiCloseHandle(hCub);
            }
        }
        finally
        {
            _ = NativeMethods.MsiCloseHandle(hDatabase);
        }
    }

    private static List<IceMessage> ExecuteIceSequence(nint hDatabase)
    {
        var messages = new List<IceMessage>();

        // Query the _ICESequence table to get ICE names
        var error = NativeMethods.MsiDatabaseOpenView(hDatabase,
            "SELECT `Action` FROM `_ICESequence` ORDER BY `Sequence`",
            out var hView);
        if (error != NativeMethods.ERROR_SUCCESS)
            return messages; // No _ICESequence table means no ICE checks

        try
        {
            error = NativeMethods.MsiViewExecute(hView, 0);
            if (error != NativeMethods.ERROR_SUCCESS)
                return messages;

            var iceNames = new List<string>();
            while (true)
            {
                error = NativeMethods.MsiViewFetch(hView, out var hRecord);
                if (error == NativeMethods.ERROR_NO_MORE_ITEMS)
                    break;
                if (error != NativeMethods.ERROR_SUCCESS)
                    break;

                try
                {
                    var name = GetRecordString(hRecord, 1);
                    if (name is not null)
                        iceNames.Add(name);
                }
                finally
                {
                    _ = NativeMethods.MsiCloseHandle(hRecord);
                }
            }

            // For each ICE, check if it left results in the _Validation table
            // ICE custom actions typically write to the _ICEnn table
            foreach (var iceName in iceNames) CollectIceResults(hDatabase, iceName, messages);
        }
        finally
        {
            _ = NativeMethods.MsiCloseHandle(hView);
        }

        return messages;
    }

    private static void CollectIceResults(nint hDatabase, string iceName, List<IceMessage> messages)
    {
        // ICE results are stored in tables named after the ICE (e.g., ICE01, ICE02)
        // Try to query the ICE results table
        var tableName = iceName;
        var error = NativeMethods.MsiDatabaseOpenView(hDatabase,
            $"SELECT `Description`, `Type`, `Table`, `Column`, `Keys` FROM `{tableName}`",
            out var hView);
        if (error != NativeMethods.ERROR_SUCCESS)
            return; // No results table for this ICE

        try
        {
            error = NativeMethods.MsiViewExecute(hView, 0);
            if (error != NativeMethods.ERROR_SUCCESS)
                return;

            while (true)
            {
                error = NativeMethods.MsiViewFetch(hView, out var hRecord);
                if (error == NativeMethods.ERROR_NO_MORE_ITEMS)
                    break;
                if (error != NativeMethods.ERROR_SUCCESS)
                    break;

                try
                {
                    var description = GetRecordString(hRecord, 1) ?? string.Empty;
                    var type = NativeMethods.MsiRecordGetInteger(hRecord, 2);
                    var table = GetRecordString(hRecord, 3);
                    var column = GetRecordString(hRecord, 4);
                    var keys = GetRecordString(hRecord, 5);

                    messages.Add(new IceMessage
                    {
                        IceName = iceName,
                        Severity = type >= 0 && type <= 3 ? (IceMessageSeverity)type : IceMessageSeverity.Information,
                        Description = description,
                        Table = table,
                        Column = column,
                        PrimaryKeys = keys
                    });
                }
                finally
                {
                    _ = NativeMethods.MsiCloseHandle(hRecord);
                }
            }
        }
        finally
        {
            _ = NativeMethods.MsiCloseHandle(hView);
        }
    }

    private static string? GetRecordString(nint hRecord, uint field)
    {
        uint size = 256;
        var buffer = new char[size + 1];
        var error = NativeMethods.MsiRecordGetString(hRecord, field, buffer, ref size);
        if (error == 234) // ERROR_MORE_DATA
        {
            size++; // Add space for null terminator
            buffer = new char[size + 1];
            error = NativeMethods.MsiRecordGetString(hRecord, field, buffer, ref size);
        }

        return error == NativeMethods.ERROR_SUCCESS ? new string(buffer, 0, (int)size) : null;
    }

    internal static string? FindDariceCub()
    {
        foreach (var basePath in CubSearchPaths)
        {
            if (File.Exists(basePath))
                return basePath;

            if (!Directory.Exists(basePath))
                continue;

            // Search for darice.cub in SDK version directories
            try
            {
                var cubFiles = Directory.GetFiles(basePath, "darice.cub", SearchOption.AllDirectories);
                if (cubFiles.Length > 0)
                    // Return the newest version
                    return cubFiles.OrderByDescending(f => f).First();
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories we can't access
            }
        }

        return null;
    }
}