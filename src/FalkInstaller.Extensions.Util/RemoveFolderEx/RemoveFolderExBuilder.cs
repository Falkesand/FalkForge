namespace FalkInstaller.Extensions.Util.RemoveFolderEx;

public sealed class RemoveFolderExBuilder
{
    private string _id = string.Empty;
    private string? _directory;
    private string? _property;
    private RemoveFolderExInstallMode _installMode = RemoveFolderExInstallMode.Uninstall;

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
            return Result<RemoveFolderExModel>.Failure(ErrorKind.Validation, "RFX002: RemoveFolderEx requires either Directory or Property.");

        return new RemoveFolderExModel
        {
            Id = _id,
            Directory = _directory,
            Property = _property,
            InstallMode = _installMode
        };
    }
}
