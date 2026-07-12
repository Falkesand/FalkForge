using FalkForge.Models;

namespace FalkForge.Builders;

// File-system content: files, folders, moves/copies/removals, GAC assemblies,
// raw binaries, and shortcuts.
public sealed partial class PackageBuilder
{
    public PackageBuilder Files(Action<FileSetBuilder> configure)
    {
        var builder = new FileSetBuilder();
        configure(builder);
        _files.AddRange(builder.Build());
        return this;
    }

    public ShortcutBuilder Shortcut(string name, string targetFile)
    {
        var builder = new ShortcutBuilder(name, targetFile, AddShortcut);
        return builder;
    }

    public PackageBuilder RemoveFile(Action<RemoveFileBuilder> configure)
    {
        var builder = new RemoveFileBuilder();
        configure(builder);
        _removeFiles.Add(builder.Build());
        return this;
    }

    public PackageBuilder CreateFolder(Action<CreateFolderBuilder> configure)
    {
        var builder = new CreateFolderBuilder();
        configure(builder);
        _createFolders.Add(builder.Build());
        return this;
    }

    public PackageBuilder MoveFile(Action<MoveFileBuilder> configure)
    {
        var builder = new MoveFileBuilder();
        configure(builder);
        _moveFiles.Add(builder.Build());
        return this;
    }

    public PackageBuilder DuplicateFile(Action<DuplicateFileBuilder> configure)
    {
        var builder = new DuplicateFileBuilder();
        configure(builder);
        _duplicateFiles.Add(builder.Build());
        return this;
    }

    public PackageBuilder GacAssembly(Action<AssemblyBuilder> configure)
    {
        var builder = new AssemblyBuilder();
        configure(builder);
        _assemblies.Add(builder.Build());
        return this;
    }

    public PackageBuilder Binary(string name, string sourcePath)
    {
        _binaries.Add(new BinaryModel { Name = name, SourcePath = sourcePath });
        return this;
    }

    internal void AddShortcut(ShortcutModel shortcut)
    {
        _shortcuts.Add(shortcut);
    }
}
