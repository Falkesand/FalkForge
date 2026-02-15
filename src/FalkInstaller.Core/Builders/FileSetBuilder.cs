namespace FalkInstaller.Builders;

using FalkInstaller.Models;

public sealed class FileSetBuilder
{
    private readonly List<FileEntrySource> _sources = [];
    private InstallPath? _targetDirectory;
    private string? _componentCondition;

    public FileSetBuilder FromDirectory(string sourcePath)
    {
        _sources.Add(new FileEntrySource(sourcePath, IsDirectory: true));
        return this;
    }

    public FileSetBuilder Add(string filePath)
    {
        _sources.Add(new FileEntrySource(filePath, IsDirectory: false));
        return this;
    }

    public FileSetBuilder To(InstallPath targetDirectory)
    {
        _targetDirectory = targetDirectory;
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
        {
            if (source.IsDirectory)
            {
                // At build time, the compiler will resolve this to actual files.
                // For now, we record the directory source as a single entry marker.
                files.Add(new FileEntryModel
                {
                    SourcePath = source.Path,
                    TargetDirectory = _targetDirectory,
                    FileName = "*",
                    IsKeyPath = files.Count == 0,
                    ComponentCondition = _componentCondition
                });
            }
            else
            {
                files.Add(new FileEntryModel
                {
                    SourcePath = source.Path,
                    TargetDirectory = _targetDirectory,
                    FileName = System.IO.Path.GetFileName(source.Path),
                    IsKeyPath = files.Count == 0,
                    ComponentCondition = _componentCondition
                });
            }
        }

        return files;
    }

    private sealed record FileEntrySource(string Path, bool IsDirectory);
}
