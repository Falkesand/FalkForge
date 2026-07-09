using FalkForge.Models;

namespace FalkForge.Builders;

// Custom actions, custom tables, and install-sequence scheduling.
public sealed partial class PackageBuilder
{
    public PackageBuilder CustomAction(string id, Action<CustomActionBuilder> configure)
    {
        var builder = new CustomActionBuilder(id);
        configure(builder);
        _customActions.Add(builder.Build());
        return this;
    }

    public PackageBuilder CustomAction(string binaryPath, string entryPoint,
        Action<CustomActionBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binaryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryPoint);

        var binaryName = Path.GetFileNameWithoutExtension(binaryPath);

        if (!_binaries.Exists(b => b.Name == binaryName))
            _binaries.Add(new BinaryModel { Name = binaryName, SourcePath = binaryPath });

        var builder = new CustomActionBuilder(entryPoint);
        builder.DllFromBinary(binaryName, entryPoint);
        configure?.Invoke(builder);
        _customActions.Add(builder.Build());
        return this;
    }

    public PackageBuilder CustomTable(Action<CustomTableBuilder> configure)
    {
        var builder = new CustomTableBuilder();
        configure(builder);
        _customTables.Add(builder.Build());
        return this;
    }

    public PackageBuilder ExecuteSequence(Action<SequenceBuilder> configure)
    {
        var builder = new SequenceBuilder(SequenceTable.InstallExecuteSequence);
        configure(builder);
        var result = builder.Build();
        if (result.IsSuccess)
            _executeSequenceActions.AddRange(result.Value);
        return this;
    }

    public PackageBuilder UISequence(Action<SequenceBuilder> configure)
    {
        var builder = new SequenceBuilder(SequenceTable.InstallUISequence);
        configure(builder);
        var result = builder.Build();
        if (result.IsSuccess)
            _uiSequenceActions.AddRange(result.Value);
        return this;
    }
}
