namespace FalkForge.Extensions.Util.RemoveFolderEx;

public sealed class RemoveFolderExBuilder
{
    private string? _directory;
    private string _id = string.Empty;
    private RemoveFolderExInstallMode _installMode = RemoveFolderExInstallMode.Uninstall;
    private string? _property;

    public RemoveFolderExBuilder Id(string id)
    {
        _id = id;
        return this;
    }

    public RemoveFolderExBuilder Directory(string directory)
    {
        _directory = directory;
        return this;
    }

    public RemoveFolderExBuilder Property(string property)
    {
        _property = property;
        return this;
    }

    public RemoveFolderExBuilder OnInstall()
    {
        _installMode = RemoveFolderExInstallMode.Install;
        return this;
    }

    public RemoveFolderExBuilder OnUninstall()
    {
        _installMode = RemoveFolderExInstallMode.Uninstall;
        return this;
    }

    public RemoveFolderExBuilder OnBoth()
    {
        _installMode = RemoveFolderExInstallMode.Both;
        return this;
    }

    internal Result<RemoveFolderExModel> Build()
    {
        if (string.IsNullOrWhiteSpace(_id))
            return Result<RemoveFolderExModel>.Failure(ErrorKind.Validation, "RFX001: RemoveFolderEx Id is required.");

        if (string.IsNullOrWhiteSpace(_directory) && string.IsNullOrWhiteSpace(_property))
            return Result<RemoveFolderExModel>.Failure(ErrorKind.Validation,
                "RFX002: RemoveFolderEx requires either Directory or Property.");

        // Path-safety: a literal Directory is known at compile time, so an obviously-unsafe value (a
        // drive root such as "C:\" or a UNC share root) fails the build loudly rather than compiling a
        // step that would delete an entire volume as SYSTEM. A Property-driven path can't be checked
        // here (its value is only known at run time) — RemoveFolderExCommandFactory applies the
        // equivalent guard inside the generated script instead.
        if (!string.IsNullOrWhiteSpace(_directory))
        {
            string full;
            string root;
            try
            {
                full = Path.GetFullPath(_directory);
                root = Path.GetPathRoot(full) ?? string.Empty;
            }
            catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
            {
                return Result<RemoveFolderExModel>.Failure(ErrorKind.Validation,
                    $"RFX003: RemoveFolderEx Directory '{_directory}' is not a valid path: {ex.Message}");
            }

            if (string.Equals(full.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) || full == "\\")
            {
                return Result<RemoveFolderExModel>.Failure(ErrorKind.Validation,
                    "RFX003: RemoveFolderEx Directory must not resolve to a filesystem root (e.g. 'C:\\' or a " +
                    "UNC share root); scope it to a specific subfolder.");
            }
        }

        // The execution seam's CustomActionData channel (needed to resolve a live Property token) only
        // feeds the deferred INSTALL action, not the uninstall one (see ExecutionStep remarks). A
        // Property combined with uninstall-time removal would silently compile into a no-op uninstall
        // action, so it is rejected here instead — fail loud, not silently broken.
        if (!string.IsNullOrWhiteSpace(_property) &&
            _installMode is RemoveFolderExInstallMode.Uninstall or RemoveFolderExInstallMode.Both)
        {
            return Result<RemoveFolderExModel>.Failure(ErrorKind.Validation,
                "RFX004: RemoveFolderEx Property cannot be combined with OnUninstall/OnBoth — the execution " +
                "seam cannot resolve a live property value for the uninstall action. Use OnInstall, or use a " +
                "literal Directory instead of Property.");
        }

        return new RemoveFolderExModel
        {
            Id = _id,
            Directory = _directory,
            Property = _property,
            InstallMode = _installMode
        };
    }
}