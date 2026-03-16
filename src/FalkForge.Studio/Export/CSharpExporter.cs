using System.Text;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Export;

public static class CSharpExporter
{
    public static Result<string> Export(StudioProject project)
    {
        if (string.IsNullOrWhiteSpace(project.Product.Name))
            return Result<string>.Failure(new Error(ErrorKind.Validation, "Product name is required."));

        if (string.IsNullOrWhiteSpace(project.Product.Manufacturer))
            return Result<string>.Failure(new Error(ErrorKind.Validation, "Manufacturer is required."));

        var sb = new StringBuilder();
        EmitUsings(sb);
        sb.AppendLine();
        sb.AppendLine("return Installer.Build(args, package =>");
        sb.AppendLine("{");

        EmitProduct(sb, project.Product);
        EmitInstallDirectory(sb, project);
        EmitFeatures(sb, project);
        EmitRegistry(sb, project);
        EmitServices(sb, project);
        EmitShortcuts(sb, project);
        EmitEnvironment(sb, project);

        sb.AppendLine("}, new MsiCompiler());");

        return Result<string>.Success(sb.ToString());
    }

    private static void EmitUsings(StringBuilder sb)
    {
        sb.AppendLine("using FalkForge;");
        sb.AppendLine("using FalkForge.Compiler.Msi;");
        sb.AppendLine("using FalkForge.Models;");
    }

    private static void EmitProduct(StringBuilder sb, ProductSection product)
    {
        sb.AppendLine($"    package.Name = {Literal(product.Name)};");
        sb.AppendLine($"    package.Manufacturer = {Literal(product.Manufacturer)};");

        if (Version.TryParse(product.Version, out _))
            sb.AppendLine($"    package.Version = new Version({FormatVersion(product.Version)});");

        if (!string.IsNullOrWhiteSpace(product.UpgradeCode) && Guid.TryParse(product.UpgradeCode, out _))
            sb.AppendLine($"    package.UpgradeCode = new Guid({Literal(product.UpgradeCode)});");

        if (!string.IsNullOrWhiteSpace(product.Description))
            sb.AppendLine($"    package.Description = {Literal(product.Description)};");

        if (!string.IsNullOrWhiteSpace(product.Comments))
            sb.AppendLine($"    package.Comments = {Literal(product.Comments)};");

        if (!string.IsNullOrWhiteSpace(product.HelpUrl))
            sb.AppendLine($"    package.HelpUrl = {Literal(product.HelpUrl)};");

        if (!string.IsNullOrWhiteSpace(product.AboutUrl))
            sb.AppendLine($"    package.AboutUrl = {Literal(product.AboutUrl)};");

        if (!string.IsNullOrWhiteSpace(product.UpdateUrl))
            sb.AppendLine($"    package.UpdateUrl = {Literal(product.UpdateUrl)};");

        if (!string.IsNullOrWhiteSpace(product.LicenseFile))
            sb.AppendLine($"    package.LicenseFile = {Literal(product.LicenseFile)};");

        if (product.Architecture != "x64")
            sb.AppendLine($"    package.Architecture = ProcessorArchitecture.{MapArchitecture(product.Architecture)};");

        if (product.Scope != "perMachine")
            sb.AppendLine($"    package.Scope = InstallScope.{MapScope(product.Scope)};");

        sb.AppendLine();
    }

    private static void EmitInstallDirectory(StringBuilder sb, StudioProject project)
    {
        if (string.IsNullOrWhiteSpace(project.InstallDirectory))
            return;

        sb.AppendLine($"    package.DefaultInstallDirectory = KnownFolder.ProgramFiles / {Literal(project.InstallDirectory)};");
        sb.AppendLine();
    }

    private static void EmitFeatures(StringBuilder sb, StudioProject project)
    {
        if (project.Features.Count == 0)
            return;

        foreach (var feature in project.Features)
            EmitFeature(sb, feature, "package", "    ");
    }

    private static void EmitFeature(StringBuilder sb, FeatureSection feature, string parent, string indent)
    {
        sb.AppendLine($"{indent}{parent}.Feature({Literal(feature.Id)}, f =>");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    f.Title = {Literal(feature.Title)};");

        if (!string.IsNullOrWhiteSpace(feature.Description))
            sb.AppendLine($"{indent}    f.Description = {Literal(feature.Description)};");

        if (feature.IsRequired)
            sb.AppendLine($"{indent}    f.IsRequired = true;");

        if (!feature.IsDefault)
            sb.AppendLine($"{indent}    f.IsDefault = false;");

        if (feature.Files.Count > 0)
            EmitFeatureFiles(sb, feature.Files, indent + "    ");

        if (feature.Features is { Count: > 0 })
        {
            foreach (var child in feature.Features)
                EmitFeature(sb, child, "f", indent + "    ");
        }

        sb.AppendLine($"{indent}}});");
        sb.AppendLine();
    }

    private static void EmitFeatureFiles(StringBuilder sb, List<FileEntry> files, string indent)
    {
        sb.AppendLine($"{indent}f.Files(files => files");
        foreach (var file in files)
            sb.AppendLine($"{indent}    .Add({Literal(file.Source)})");

        var targetDir = files.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.TargetDirectory))?.TargetDirectory;
        if (!string.IsNullOrWhiteSpace(targetDir))
            sb.AppendLine($"{indent}    .To(KnownFolder.ProgramFiles / {Literal(targetDir)}));");
        else
            sb.AppendLine($"{indent}    .To(KnownFolder.ProgramFiles));");
    }

    private static void EmitRegistry(StringBuilder sb, StudioProject project)
    {
        if (project.Registry.Count == 0)
            return;

        var grouped = project.Registry
            .GroupBy(r => new { r.Root, r.Key });

        foreach (var group in grouped)
        {
            sb.AppendLine($"    package.Registry(r => r");
            sb.AppendLine($"        .Key(RegistryRoot.{group.Key.Root}, {Literal(group.Key.Key)}, k =>");
            sb.AppendLine("        {");
            foreach (var entry in group)
            {
                if (entry.ValueType == "DWord" && int.TryParse(entry.Value, out var intValue))
                    sb.AppendLine($"            k.DWord({Literal(entry.ValueName)}, {intValue});");
                else
                    sb.AppendLine($"            k.Value({Literal(entry.ValueName)}, {Literal(entry.Value)});");
            }
            sb.AppendLine("        }));");
            sb.AppendLine();
        }
    }

    private static void EmitServices(StringBuilder sb, StudioProject project)
    {
        if (project.Services.Count == 0)
            return;

        foreach (var service in project.Services)
        {
            sb.AppendLine($"    package.Service({Literal(service.Name)}, svc =>");
            sb.AppendLine("    {");
            sb.AppendLine($"        svc.DisplayName = {Literal(service.DisplayName)};");
            sb.AppendLine($"        svc.Executable = {Literal(service.Executable)};");

            if (!string.IsNullOrWhiteSpace(service.Description))
                sb.AppendLine($"        svc.Description = {Literal(service.Description)};");

            if (service.StartMode != "Automatic")
                sb.AppendLine($"        svc.StartMode = ServiceStartMode.{service.StartMode};");

            if (service.Account != "LocalSystem")
                sb.AppendLine($"        svc.Account = ServiceAccount.{service.Account};");

            sb.AppendLine("    });");
            sb.AppendLine();
        }
    }

    private static void EmitShortcuts(StringBuilder sb, StudioProject project)
    {
        if (project.Shortcuts.Count == 0)
            return;

        foreach (var shortcut in project.Shortcuts)
        {
            sb.Append($"    package.Shortcut({Literal(shortcut.Name)}, {Literal(shortcut.TargetFile)})");

            if (!string.IsNullOrWhiteSpace(shortcut.Description))
                sb.Append($"\r\n        .WithDescription({Literal(shortcut.Description)})");

            if (!string.IsNullOrWhiteSpace(shortcut.Arguments))
                sb.Append($"\r\n        .WithArguments({Literal(shortcut.Arguments)})");

            if (!string.IsNullOrWhiteSpace(shortcut.IconFile))
                sb.Append($"\r\n        .WithIcon({Literal(shortcut.IconFile)})");

            if (!string.IsNullOrWhiteSpace(shortcut.WorkingDirectory))
                sb.Append($"\r\n        .WithWorkingDirectory({Literal(shortcut.WorkingDirectory)})");

            if (shortcut.Desktop)
                sb.Append("\r\n        .OnDesktop()");

            if (shortcut.StartMenu)
            {
                if (!string.IsNullOrWhiteSpace(shortcut.StartMenuSubfolder))
                    sb.Append($"\r\n        .OnStartMenu({Literal(shortcut.StartMenuSubfolder)})");
                else
                    sb.Append("\r\n        .OnStartMenu()");
            }

            if (shortcut.Startup)
                sb.Append("\r\n        .OnStartup()");

            sb.AppendLine(";");
            sb.AppendLine();
        }
    }

    private static void EmitEnvironment(StringBuilder sb, StudioProject project)
    {
        if (project.Environment.Count == 0)
            return;

        foreach (var env in project.Environment)
        {
            sb.AppendLine($"    package.EnvironmentVariable({Literal(env.Name)}, {Literal(env.Value)}, ev =>");
            sb.AppendLine("    {");

            if (!env.IsSystem)
                sb.AppendLine("        ev.IsSystem = false;");

            if (env.Action != "Set")
                sb.AppendLine($"        ev.Action = EnvironmentVariableAction.{env.Action};");

            sb.AppendLine("    });");
            sb.AppendLine();
        }
    }

    private static string Literal(string? value)
    {
        if (value is null)
            return "\"\"";

        var escaped = value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");

        return $"\"{escaped}\"";
    }

    private static string FormatVersion(string version)
    {
        var parts = version.Split('.');
        return string.Join(", ", parts);
    }

    private static string MapArchitecture(string architecture) => architecture.ToLowerInvariant() switch
    {
        "x86" => "X86",
        "x64" => "X64",
        "arm64" => "Arm64",
        _ => "X64"
    };

    private static string MapScope(string scope) => scope.ToLowerInvariant() switch
    {
        "peruser" => "PerUser",
        "permachine" => "PerMachine",
        _ => "PerMachine"
    };
}
