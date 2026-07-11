namespace FalkForge.Extensions.Util.FileShare;

public sealed class FileShareBuilder
{
    private readonly List<FileSharePermission> _permissions = [];
    private string? _description;
    private string _directory = string.Empty;
    private string _id = string.Empty;
    private string _name = string.Empty;

    public FileShareBuilder Id(string id)
    {
        _id = id;
        return this;
    }

    public FileShareBuilder Name(string name)
    {
        _name = name;
        return this;
    }

    public FileShareBuilder Description(string description)
    {
        _description = description;
        return this;
    }

    public FileShareBuilder Directory(string directory)
    {
        _directory = directory;
        return this;
    }

    public FileShareBuilder GrantRead(string user)
    {
        _permissions.Add(new FileSharePermission { User = user, Permission = FileSharePermissionLevel.Read });
        return this;
    }

    public FileShareBuilder GrantChange(string user)
    {
        _permissions.Add(new FileSharePermission { User = user, Permission = FileSharePermissionLevel.Change });
        return this;
    }

    public FileShareBuilder GrantFull(string user)
    {
        _permissions.Add(new FileSharePermission { User = user, Permission = FileSharePermissionLevel.Full });
        return this;
    }

    internal Result<FileShareModel> Build()
    {
        if (string.IsNullOrWhiteSpace(_id))
            return Result<FileShareModel>.Failure(ErrorKind.Validation, "FSH001: FileShare Id is required.");

        if (string.IsNullOrWhiteSpace(_name))
            return Result<FileShareModel>.Failure(ErrorKind.Validation, "FSH002: FileShare Name is required.");

        if (string.IsNullOrWhiteSpace(_directory))
            return Result<FileShareModel>.Failure(ErrorKind.Validation, "FSH003: FileShare Directory is required.");

        // The Directory (shared path) is emitted as a live, double-quoted trailing argument to the
        // deferred action (so an MSI Formatted token like [INSTALLDIR] resolves at install time). A
        // double quote is an illegal Windows path character and would break out of that quoting, so
        // reject it as defense in depth against a malformed author value.
        if (_directory.Contains('"', StringComparison.Ordinal))
            return Result<FileShareModel>.Failure(ErrorKind.Validation,
                "FSH004: FileShare Directory must not contain a double-quote character.");

        return new FileShareModel
        {
            Id = _id,
            Name = _name,
            Description = _description,
            Directory = _directory,
            Permissions = _permissions
        };
    }
}