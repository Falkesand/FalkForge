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

    public string Emit(PackageModel package)
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
        EmitRegistryEntries(package.RegistryEntries);
        EmitServices(package.Services);
        EmitShortcuts(package.Shortcuts);
        EmitProperties(package.Properties);
        EmitMajorUpgrade(package.MajorUpgrade);

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
            AppendLine($"f.Title({Quote(feature.Title)});");
            if (feature.Description is not null)
                AppendLine($"f.Description({Quote(feature.Description)});");
            if (feature.IsRequired)
                AppendLine("f.Required();");

            foreach (var child in feature.Children)
            {
                AppendLine($"f.Child({Quote(child.Id)}, c =>");
                AppendLine("{");
                _indent++;
                AppendLine($"c.Title({Quote(child.Title)});");
                if (child.Description is not null)
                    AppendLine($"c.Description({Quote(child.Description)});");
                _indent--;
                AppendLine("});");
            }

            _indent--;
            AppendLine("});");
            AppendLine();
        }
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

            var valueStr = entry.Value switch
            {
                string s => Quote(s),
                int i => i.ToString(CultureInfo.InvariantCulture),
                _ => "null"
            };

            AppendLine($"r.Key({rootStr}, {Quote(entry.Key)})");
            _indent++;
            if (entry.ValueName is not null)
                AppendLine($".Name({Quote(entry.ValueName)})");
            AppendLine($".Value({valueStr});");
            _indent--;
        }

        _indent--;
        AppendLine("});");
        AppendLine();
    }

    private void EmitServices(IReadOnlyList<ServiceModel> services)
    {
        foreach (var service in services)
        {
            AppendLine($"builder.Service({Quote(service.Name)}, s =>");
            AppendLine("{");
            _indent++;
            AppendLine($"s.DisplayName({Quote(service.DisplayName)});");
            AppendLine($"s.Executable({Quote(service.Executable)});");
            if (service.Description is not null)
                AppendLine($"s.Description({Quote(service.Description)});");
            if (service.StartMode != ServiceStartMode.Automatic)
                AppendLine($"s.StartMode(ServiceStartMode.{service.StartMode});");
            if (service.Account != ServiceAccount.LocalSystem)
                AppendLine($"s.Account(ServiceAccount.{service.Account});");
            _indent--;
            AppendLine("});");
            AppendLine();
        }
    }

    private void EmitShortcuts(IReadOnlyList<ShortcutModel> shortcuts)
    {
        foreach (var shortcut in shortcuts)
        {
            AppendLine($"builder.Shortcut({Quote(shortcut.Name)}, {Quote(shortcut.TargetFile)})");
            _indent++;
            foreach (var location in shortcut.Locations)
            {
                AppendLine($".Location(ShortcutLocation.{location})");
            }
            if (shortcut.Description is not null)
                AppendLine($".Description({Quote(shortcut.Description)})");
            if (shortcut.Arguments is not null)
                AppendLine($".Arguments({Quote(shortcut.Arguments)})");
            AppendLine(".Add();");
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
        if (majorUpgrade.AllowDowngrades)
            AppendLine("u.AllowDowngrades();");
        if (majorUpgrade.AllowSameVersionUpgrades)
            AppendLine("u.AllowSameVersionUpgrades();");
        if (majorUpgrade.DowngradeErrorMessage is not null)
            AppendLine($"u.DowngradeErrorMessage({Quote(majorUpgrade.DowngradeErrorMessage)});");
        _indent--;
        AppendLine("});");
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
