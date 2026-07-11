using FalkForge.Models;

namespace FalkForge.Builders;

public sealed class FileSetBuilder
{
    private readonly List<FileEntrySource> _sources = [];
    private string? _componentCondition;
    private bool _neverOverwrite;
    private bool _permanent;
    private bool _vital = true;
    private InstallPath? _targetDirectory;

    public FileSetBuilder FromDirectory(string sourcePath)
    {
        _sources.Add(new FileEntrySource(sourcePath, true));
        return this;
    }

    public FileSetBuilder Add(string filePath)
    {
        _sources.Add(new FileEntrySource(filePath, false));
        return this;
    }

    public FileSetBuilder To(InstallPath targetDirectory)
    {
        _targetDirectory = targetDirectory;
        return this;
    }

    public FileSetBuilder NeverOverwrite()
    {
        _neverOverwrite = true;
        return this;
    }

    public FileSetBuilder Permanent()
    {
        _permanent = true;
        return this;
    }

    /// <summary>
    /// Marks every file in this set as non-vital: a copy failure during install is skipped
    /// instead of aborting the install. Files are vital by default.
    /// </summary>
    public FileSetBuilder NotVital()
    {
        _vital = false;
        return this;
    }

    public FileSetBuilder ComponentCondition(string condition)
    {
        _componentCondition = condition;
        return this;
    }

    internal IReadOnlyList<FileEntryModel> Build()
    {
        if (_targetDirectory is null)
            throw new InvalidOperationException("Target directory must be specified using To().");

        var files = new List<FileEntryModel>();

        foreach (var source in _sources)
            if (source.IsDirectory)
                // At build time, the compiler will resolve this to actual files.
                // For now, we record the directory source as a single entry marker.
                files.Add(new FileEntryModel
                {
                    SourcePath = source.Path,
                    TargetDirectory = _targetDirectory,
                    FileName = "*",
                    IsKeyPath = files.Count == 0,
                    NeverOverwrite = _neverOverwrite,
                    Permanent = _permanent,
                    Vital = _vital,
                    ComponentCondition = _componentCondition
                });
            else
                files.Add(new FileEntryModel
                {
                    SourcePath = source.Path,
                    TargetDirectory = _targetDirectory,
                    FileName = Path.GetFileName(source.Path),
                    IsKeyPath = files.Count == 0,
                    NeverOverwrite = _neverOverwrite,
                    Permanent = _permanent,
                    Vital = _vital,
                    ComponentCondition = _componentCondition
                });

        return files;
    }

    private sealed record FileEntrySource(string Path, bool IsDirectory);
}