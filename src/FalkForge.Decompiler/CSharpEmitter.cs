using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using FalkForge.Models;

namespace FalkForge.Decompiler;

/// <summary>
/// Converts a <see cref="PackageModel"/> into fluent C# source code
/// that recreates the installer definition using the FalkForge builder API.
/// </summary>
public sealed class CSharpEmitter
{
    private readonly StringBuilder _sb = new();
    private int _indent;

    /// <summary>
    /// Reverse map from an MSI known-folder token to the C# <see cref="KnownFolder"/>
    /// static member name, so the emitter can render a fully-qualified member access
    /// (e.g. <c>"ProgramFilesFolder"</c> → <c>KnownFolder.ProgramFiles</c>).
    /// </summary>
    private static readonly FrozenDictionary<string, string> TokenToMemberName =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ProgramFilesFolder"]    = "ProgramFiles",
            ["ProgramFiles64Folder"]  = "ProgramFiles64",
            ["CommonAppDataFolder"]   = "CommonAppData",
            ["LocalAppDataFolder"]    = "LocalAppData",
            ["AppDataFolder"]         = "AppData",
            ["SystemFolder"]          = "SystemFolder",
            ["System64Folder"]        = "System64Folder",
            ["WindowsFolder"]         = "WindowsFolder",
            ["TempFolder"]            = "TempFolder",
            ["DesktopFolder"]         = "DesktopFolder",
            ["StartMenuFolder"]       = "StartMenuFolder",
            ["ProgramMenuFolder"]     = "ProgramMenuFolder",
            ["StartupFolder"]         = "StartupFolder",
            ["CommonFilesFolder"]     = "CommonFilesFolder",
            ["CommonFiles64Folder"]   = "CommonFiles64Folder",
            ["FontsFolder"]           = "FontsFolder",
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// Emits the fluent C# source for <paramref name="package"/>, throwing
    /// <see cref="InvalidOperationException"/> when a known-folder root token has no
    /// <see cref="KnownFolder"/> member (emitting it would produce non-compiling code).
    /// Prefer <see cref="TryEmit"/> on paths that must not let the exception escape
    /// (e.g. the migration generator, which returns the failure via <c>Result</c>).
    /// </summary>
    public string Emit(PackageModel package)
    {
        var result = TryEmit(package);
        if (result.IsFailure)
            throw new InvalidOperationException(result.Error.Message);
        return result.Value;
    }

    /// <summary>
    /// Emits the fluent C# source for <paramref name="package"/> as a <see cref="Result{T}"/>:
    /// a <see cref="ErrorKind.CompilationError"/> failure (naming the unsupported token) when a
    /// known-folder root token has no <see cref="KnownFolder"/> member, instead of throwing.
    /// </summary>
    public Result<string> TryEmit(PackageModel package)
    {
        ArgumentNullException.ThrowIfNull(package);

        // Fail before writing any output if any file targets an unmapped root token —
        // emitting it would produce non-compiling C# (no matching KnownFolder member).
        foreach (var file in package.Files)
        {
            if (file.FileName == "*")
                continue;
            var token = file.TargetDirectory.Root.Token;
            if (!TokenToMemberName.ContainsKey(token))
                return Result<string>.Failure(
                    ErrorKind.CompilationError,
                    $"Cannot render unsupported KnownFolder root token '{token}'. " +
                    "The decompiled installer references a folder FalkForge cannot map to a " +
                    "KnownFolder member; re-author this file's install location manually.");
        }

        return Result<string>.Success(EmitCore(package));
    }

    private string EmitCore(PackageModel package)
    {
        _sb.Clear();
        _indent = 0;

        AppendLine("using FalkForge;");
        AppendLine("using FalkForge.Builders;");
        AppendLine("using FalkForge.Models;");
        AppendLine();

        AppendLine($"var builder = new PackageBuilder();");
        AppendLine($"builder.Name = {Quote(package.Name)};");
        AppendLine($"builder.Manufacturer = {Quote(package.Manufacturer)};");
        AppendLine($"builder.Version = new Version({package.Version.Major}, {package.Version.Minor}, {package.Version.Build});");

        if (package.UpgradeCode != Guid.Empty)
            AppendLine($"builder.UpgradeCode = new Guid({Quote(package.UpgradeCode.ToString())});");

        if (package.Scope != InstallScope.PerMachine)
            AppendLine($"builder.Scope = InstallScope.{package.Scope};");

        if (package.Architecture != ProcessorArchitecture.X64)
            AppendLine($"builder.Architecture = ProcessorArchitecture.{package.Architecture};");

        if (package.Description is not null)
            AppendLine($"builder.Description = {Quote(package.Description)};");

        AppendLine();

        EmitFeatures(package.Features);
        EmitFiles(package.Files);
        EmitRegistryEntries(package.RegistryEntries);
        EmitServices(package.Services);
        EmitShortcuts(package.Shortcuts);
        EmitProperties(package.Properties);
        EmitMajorUpgrade(package.MajorUpgrade);
        EmitDowngrade(package.Downgrade);

        AppendLine("var model = builder.Build();");

        return _sb.ToString();
    }

    private void EmitFeatures(IReadOnlyList<FeatureModel> features)
    {
        foreach (var feature in features)
        {
            AppendLine($"builder.Feature({Quote(feature.Id)}, f =>");
            AppendLine("{");
            _indent++;
            // FeatureBuilder exposes Title/Description/IsRequired as settable PROPERTIES,
            // not fluent methods, and nests children via Feature(id, ...), not Child(...).
            EmitFeatureBody("f", feature);

            foreach (var child in feature.Children)
            {
                AppendLine($"f.Feature({Quote(child.Id)}, c =>");
                AppendLine("{");
                _indent++;
                EmitFeatureBody("c", child);
                _indent--;
                AppendLine("});");
            }

            _indent--;
            AppendLine("});");
            AppendLine();
        }
    }

    private void EmitFeatureBody(string varName, FeatureModel feature)
    {
        AppendLine($"{varName}.Title = {Quote(feature.Title)};");
        if (feature.Description is not null)
            AppendLine($"{varName}.Description = {Quote(feature.Description)};");
        if (feature.IsRequired)
            AppendLine($"{varName}.IsRequired = true;");
    }

    private void EmitFiles(IReadOnlyList<FileEntryModel> files)
    {
        // Skip wildcard FromDirectory markers (FileName == "*"): they are not real
        // payload files, so emitting .Add("*") would create a non-resolvable key.
        // Decompiled models never produce these (the reconstructor always sets a
        // concrete long name), but the emitter is also used by plain "forge decompile".
        var realFiles = files.Where(f => f.FileName != "*").ToList();
        if (realFiles.Count == 0)
            return;

        // Group by target directory so files installed to the same location share one
        // Files(...) block, matching how a hand-authored installer reads.
        foreach (var group in realFiles.GroupBy(f => f.TargetDirectory))
        {
            var rendered = RenderInstallPath(group.Key);

            AppendLine("builder.Files(files =>");
            AppendLine("{");
            _indent++;
            foreach (var file in group)
            {
                var key = PayloadPath.For(file.TargetDirectory.Segments, file.FileName);
                AppendLine($"files.Add({Quote(key)}).To({rendered});");
            }
            _indent--;
            AppendLine("});");
            AppendLine();
        }
    }

    /// <summary>
    /// Renders an <see cref="InstallPath"/> as a C# expression
    /// (e.g. <c>KnownFolder.ProgramFiles / "Demo" / "App"</c>).
    /// Throws when the root token has no <see cref="KnownFolder"/> member — emitting
    /// an unmapped folder would produce non-compiling code, so we fail loud instead.
    /// </summary>
    private static string RenderInstallPath(InstallPath path)
    {
        var token = path.Root.Token;
        if (!TokenToMemberName.TryGetValue(token, out var member))
            throw new InvalidOperationException(
                $"Cannot render unknown KnownFolder token '{token}'.");

        var sb = new StringBuilder("KnownFolder.").Append(member);
        foreach (var segment in path.Segments)
            sb.Append(" / ").Append(Quote(segment));

        return sb.ToString();
    }

    private void EmitRegistryEntries(IReadOnlyList<RegistryEntryModel> entries)
    {
        if (entries.Count == 0)
            return;

        AppendLine("builder.Registry(r =>");
        AppendLine("{");
        _indent++;

        foreach (var entry in entries)
        {
            var rootStr = entry.Root switch
            {
                RegistryRoot.LocalMachine => "RegistryRoot.LocalMachine",
                RegistryRoot.CurrentUser => "RegistryRoot.CurrentUser",
                RegistryRoot.ClassesRoot => "RegistryRoot.ClassesRoot",
                RegistryRoot.Users => "RegistryRoot.Users",
                _ => "RegistryRoot.LocalMachine"
            };

            // RegistryBuilder.Key takes (root, key, Action<RegistryKeyBuilder>); the inner
            // builder writes values via DWord(name, int), Value(name, string),
            // or DefaultValue(string) when there is no value name.
            AppendLine($"r.Key({rootStr}, {Quote(entry.Key)}, k =>");
            AppendLine("{");
            _indent++;
            EmitRegistryValue(entry);
            _indent--;
            AppendLine("});");
        }

        _indent--;
        AppendLine("});");
        AppendLine();
    }

    private void EmitRegistryValue(RegistryEntryModel entry)
    {
        if (entry.ValueType == RegistryValueType.DWord)
        {
            // DWord values are stored as int; ValueName is required for a named DWord.
            var dword = entry.Value switch
            {
                int i => i.ToString(CultureInfo.InvariantCulture),
                _ => "0"
            };
            AppendLine($"k.DWord({Quote(entry.ValueName ?? string.Empty)}, {dword});");
            return;
        }

        var stringValue = entry.Value switch
        {
            string s => s,
            int i => i.ToString(CultureInfo.InvariantCulture),
            _ => string.Empty
        };

        if (entry.ValueName is null)
            AppendLine($"k.DefaultValue({Quote(stringValue)});");
        else
            AppendLine($"k.Value({Quote(entry.ValueName)}, {Quote(stringValue)});");
    }

    private void EmitServices(IReadOnlyList<ServiceModel> services)
    {
        foreach (var service in services)
        {
            AppendLine($"builder.Service({Quote(service.Name)}, s =>");
            AppendLine("{");
            _indent++;
            // ServiceBuilder exposes these as settable PROPERTIES, not fluent methods.
            AppendLine($"s.DisplayName = {Quote(service.DisplayName)};");
            AppendLine($"s.Executable = {Quote(service.Executable)};");
            if (service.Description is not null)
                AppendLine($"s.Description = {Quote(service.Description)};");
            if (service.StartMode != ServiceStartMode.Automatic)
                AppendLine($"s.StartMode = ServiceStartMode.{service.StartMode};");
            if (service.Account != ServiceAccount.LocalSystem)
                AppendLine($"s.Account = ServiceAccount.{service.Account};");
            _indent--;
            AppendLine("});");
            AppendLine();
        }
    }

    private void EmitShortcuts(IReadOnlyList<ShortcutModel> shortcuts)
    {
        foreach (var shortcut in shortcuts)
        {
            // ShortcutBuilder uses WithDescription/WithArguments for metadata and an
            // OnDesktop/OnStartMenu/OnStartup terminal call that registers the shortcut.
            // The terminal On* call must come LAST: each one snapshots the current builder
            // state into a ShortcutModel, so the metadata has to be set beforehand.
            AppendLine($"builder.Shortcut({Quote(shortcut.Name)}, {Quote(shortcut.TargetFile)})");
            _indent++;
            if (shortcut.Description is not null)
                AppendLine($".WithDescription({Quote(shortcut.Description)})");
            if (shortcut.Arguments is not null)
                AppendLine($".WithArguments({Quote(shortcut.Arguments)})");

            var location = shortcut.Locations.Count > 0
                ? shortcut.Locations[0]
                : ShortcutLocation.StartMenu;
            var terminal = location switch
            {
                ShortcutLocation.Desktop => ".OnDesktop();",
                ShortcutLocation.Startup => ".OnStartup();",
                _ => ".OnStartMenu();"
            };
            AppendLine(terminal);
            _indent--;
            AppendLine();
        }
    }

    private void EmitProperties(IReadOnlyList<PropertyModel> properties)
    {
        foreach (var prop in properties)
        {
            AppendLine($"builder.Property({Quote(prop.Name)}, {Quote(prop.Value)});");
        }
        if (properties.Count > 0)
            AppendLine();
    }

    private void EmitMajorUpgrade(MajorUpgradeModel? majorUpgrade)
    {
        if (majorUpgrade is null)
            return;

        AppendLine("builder.MajorUpgrade(u =>");
        AppendLine("{");
        _indent++;
        if (majorUpgrade.AllowSameVersionUpgrades)
            AppendLine("u.AllowSameVersionUpgrades();");
        _indent--;
        AppendLine("});");
        AppendLine();
    }

    private void EmitDowngrade(DowngradeModel? downgrade)
    {
        if (downgrade is null)
            return;

        if (downgrade.AllowDowngrades)
        {
            AppendLine("builder.Downgrade(d => d.Allow());");
        }
        else
        {
            // DowngradeBuilder.Block requires a message argument (no parameterless overload).
            AppendLine($"builder.Downgrade(d => d.Block({Quote(downgrade.ErrorMessage ?? string.Empty)}));");
        }
        AppendLine();
    }

    private void AppendLine(string line = "")
    {
        if (string.IsNullOrEmpty(line))
        {
            _sb.AppendLine();
            return;
        }

        _sb.Append(new string(' ', _indent * 4));
        _sb.AppendLine(line);
    }

    private static string Quote(string value)
    {
        var escaped = value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
        return $"\"{escaped}\"";
    }
}
