namespace FalkForge.Extensions.Util.InternetShortcut;

public sealed class InternetShortcutBuilder
{
    private string _directory = string.Empty;
    private string? _iconFile;
    private int _iconIndex;
    private string _id = string.Empty;
    private string _name = string.Empty;
    private string _target = string.Empty;

    public InternetShortcutBuilder Id(string id)
    {
        _id = id;
        return this;
    }

    public InternetShortcutBuilder Name(string name)
    {
        _name = name;
        return this;
    }

    public InternetShortcutBuilder Target(string url)
    {
        _target = url;
        return this;
    }

    public InternetShortcutBuilder Directory(string directory)
    {
        _directory = directory;
        return this;
    }

    public InternetShortcutBuilder Icon(string iconFile, int iconIndex = 0)
    {
        _iconFile = iconFile;
        _iconIndex = iconIndex;
        return this;
    }

    internal Result<InternetShortcutModel> Build()
    {
        if (string.IsNullOrWhiteSpace(_id))
            return Result<InternetShortcutModel>.Failure(ErrorKind.Validation,
                "ISC001: InternetShortcut Id is required.");

        if (string.IsNullOrWhiteSpace(_name))
            return Result<InternetShortcutModel>.Failure(ErrorKind.Validation,
                "ISC002: InternetShortcut Name is required.");

        if (string.IsNullOrWhiteSpace(_target))
            return Result<InternetShortcutModel>.Failure(ErrorKind.Validation,
                "ISC003: InternetShortcut Target URL is required.");

        if (string.IsNullOrWhiteSpace(_directory))
            return Result<InternetShortcutModel>.Failure(ErrorKind.Validation,
                "ISC004: InternetShortcut Directory is required.");

        // The Directory is emitted as a live, double-quoted trailing argument to the deferred action
        // (so an MSI Formatted token like [INSTALLDIR] resolves at install time). A double quote is an
        // illegal Windows path character and would break out of that quoting, so reject it as defense in
        // depth against a malformed author value.
        if (_directory.Contains('"', StringComparison.Ordinal))
            return Result<InternetShortcutModel>.Failure(ErrorKind.Validation,
                "ISC005: InternetShortcut Directory must not contain a double-quote character.");

        return new InternetShortcutModel
        {
            Id = _id,
            Name = _name,
            Target = _target,
            Directory = _directory,
            IconFile = _iconFile,
            IconIndex = _iconIndex
        };
    }
}