using System.IO;
using System.Xml.Linq;
using FalkForge.Studio.Project;

namespace FalkForge.Studio.Import;

internal static class WixImporter
{
    private static readonly XNamespace WixV3Ns = "http://schemas.microsoft.com/wix/2006/wi";
    private static readonly XNamespace WixV4Ns = "http://wixtoolset.org/schemas/v4/wxs";

    public static Result<StudioProject> Import(string wxsPath)
    {
        if (!File.Exists(wxsPath))
            return Result<StudioProject>.Failure(ErrorKind.FileNotFound, $"File not found: {wxsPath}");

        try
        {
            var doc = XDocument.Load(wxsPath);
            return ImportFromDocument(doc);
        }
        catch (System.Xml.XmlException ex)
        {
            return Result<StudioProject>.Failure(ErrorKind.Validation, $"Invalid XML: {ex.Message}");
        }
    }

    internal static Result<StudioProject> ImportFromDocument(XDocument doc)
    {
        var root = doc.Root;
        if (root is null)
            return Result<StudioProject>.Failure(ErrorKind.Validation, "XML document has no root element.");

        var ns = DetectNamespace(root);
        if (ns is null)
            return Result<StudioProject>.Failure(ErrorKind.Validation, "Not a recognized WiX source file. Expected WiX v3 or v4 namespace.");

        var project = new StudioProject();
        var warnings = new List<string>();

        // Find the product/package element (v3: Product, v4: Package)
        var productEl = root.Element(ns + "Product") ?? root.Element(ns + "Package");
        if (productEl is not null)
        {
            MapProduct(productEl, ns, project);
        }
        else
        {
            warnings.Add("No Product or Package element found.");
        }

        // Build component-to-file map for feature resolution
        var componentFiles = new Dictionary<string, List<FileEntry>>(StringComparer.OrdinalIgnoreCase);
        var componentServices = new Dictionary<string, List<ServiceSection>>(StringComparer.OrdinalIgnoreCase);
        var componentRegistry = new Dictionary<string, List<RegistryEntrySection>>(StringComparer.OrdinalIgnoreCase);
        var componentShortcuts = new Dictionary<string, List<ShortcutSection>>(StringComparer.OrdinalIgnoreCase);
        var componentEnvironment = new Dictionary<string, List<EnvironmentVariableSection>>(StringComparer.OrdinalIgnoreCase);

        // Resolve directory tree for install directory detection
        var installDir = ResolveInstallDirectory(root, ns);
        if (installDir is not null)
            project.InstallDirectory = installDir;

        // Collect all components from the entire document
        foreach (var comp in root.Descendants(ns + "Component"))
        {
            var compId = comp.Attribute("Id")?.Value ?? "";

            foreach (var fileEl in comp.Elements(ns + "File"))
            {
                var entry = MapFile(fileEl);
                if (!componentFiles.TryGetValue(compId, out var list))
                {
                    list = [];
                    componentFiles[compId] = list;
                }
                list.Add(entry);
            }

            foreach (var svcEl in comp.Elements(ns + "ServiceInstall"))
            {
                var svc = MapService(svcEl);
                if (!componentServices.TryGetValue(compId, out var list))
                {
                    list = [];
                    componentServices[compId] = list;
                }
                list.Add(svc);
                project.Services.Add(svc);
            }

            foreach (var regKeyEl in comp.Elements(ns + "RegistryKey"))
            {
                foreach (var regValEl in regKeyEl.Elements(ns + "RegistryValue"))
                {
                    var entry = MapRegistryEntry(regKeyEl, regValEl);
                    if (!componentRegistry.TryGetValue(compId, out var list))
                    {
                        list = [];
                        componentRegistry[compId] = list;
                    }
                    list.Add(entry);
                    project.Registry.Add(entry);
                }
            }

            // WiX v4 allows RegistryValue directly in Component without RegistryKey wrapper
            foreach (var regValEl in comp.Elements(ns + "RegistryValue"))
            {
                var entry = MapRegistryValueDirect(regValEl);
                if (!componentRegistry.TryGetValue(compId, out var list))
                {
                    list = [];
                    componentRegistry[compId] = list;
                }
                list.Add(entry);
                project.Registry.Add(entry);
            }

            foreach (var shortcutEl in comp.Elements(ns + "Shortcut"))
            {
                var shortcut = MapShortcut(shortcutEl);
                if (!componentShortcuts.TryGetValue(compId, out var list))
                {
                    list = [];
                    componentShortcuts[compId] = list;
                }
                list.Add(shortcut);
                project.Shortcuts.Add(shortcut);
            }

            foreach (var envEl in comp.Elements(ns + "Environment"))
            {
                var env = MapEnvironment(envEl);
                if (!componentEnvironment.TryGetValue(compId, out var list))
                {
                    list = [];
                    componentEnvironment[compId] = list;
                }
                list.Add(env);
                project.Environment.Add(env);
            }
        }

        // Map features with their files (linked via ComponentRef)
        var searchRoot = productEl ?? root;
        foreach (var featureEl in searchRoot.Elements(ns + "Feature"))
        {
            project.Features.Add(MapFeature(featureEl, ns, componentFiles));
        }

        // If no features found but we have files, create a default feature
        if (project.Features.Count == 0 && componentFiles.Count > 0)
        {
            var defaultFeature = new FeatureSection
            {
                Id = "DefaultFeature",
                Title = "Complete",
                Files = componentFiles.Values.SelectMany(f => f).ToList()
            };
            project.Features.Add(defaultFeature);
        }

        // Custom actions
        foreach (var caEl in root.Descendants(ns + "CustomAction"))
        {
            project.CustomActions.Add(MapCustomAction(caEl));
        }

        return Result<StudioProject>.Success(project);
    }

    private static XNamespace? DetectNamespace(XElement root)
    {
        var rootNs = root.Name.Namespace;
        if (rootNs == WixV3Ns) return WixV3Ns;
        if (rootNs == WixV4Ns) return WixV4Ns;

        // Check if root is <Wix> with a namespace
        if (root.Name.LocalName == "Wix" || root.Name.LocalName == "Package" || root.Name.LocalName == "Product")
        {
            if (rootNs == XNamespace.None)
            {
                // WiX v4 allows no namespace in some cases
                return WixV4Ns;
            }
        }

        return null;
    }

    private static void MapProduct(XElement productEl, XNamespace ns, StudioProject project)
    {
        var product = project.Product;
        product.Name = productEl.Attribute("Name")?.Value ?? product.Name;
        product.Manufacturer = productEl.Attribute("Manufacturer")?.Value ?? product.Manufacturer;
        product.Version = productEl.Attribute("Version")?.Value ?? product.Version;
        product.UpgradeCode = productEl.Attribute("UpgradeCode")?.Value;

        // v4 Package may have Scope attribute
        var scope = productEl.Attribute("Scope")?.Value;
        if (scope is not null)
        {
            product.Scope = scope.Equals("perUser", StringComparison.OrdinalIgnoreCase) ? "perUser" : "perMachine";
        }

        // v3 Package child element may have Description, Platform
        var packageEl = productEl.Element(ns + "Package");
        if (packageEl is not null)
        {
            product.Description = packageEl.Attribute("Description")?.Value ?? product.Description;
            var platform = packageEl.Attribute("Platform")?.Value;
            if (platform is not null)
                product.Architecture = platform.Equals("x64", StringComparison.OrdinalIgnoreCase) ? "x64" : "x86";

            var installScope = packageEl.Attribute("InstallScope")?.Value;
            if (installScope is not null)
                product.Scope = installScope.Equals("perUser", StringComparison.OrdinalIgnoreCase) ? "perUser" : "perMachine";
        }

    }

    private static string? ResolveInstallDirectory(XElement root, XNamespace ns)
    {
        // Look for INSTALLDIR or INSTALLFOLDER in Directory tree
        foreach (var dirEl in root.Descendants(ns + "Directory"))
        {
            var id = dirEl.Attribute("Id")?.Value;
            if (id is not null &&
                (id.Equals("INSTALLDIR", StringComparison.OrdinalIgnoreCase) ||
                 id.Equals("INSTALLFOLDER", StringComparison.OrdinalIgnoreCase)))
            {
                var name = dirEl.Attribute("Name")?.Value;
                return name;
            }
        }

        // v4 uses StandardDirectory
        foreach (var stdDir in root.Descendants(ns + "StandardDirectory"))
        {
            var id = stdDir.Attribute("Id")?.Value;
            if (id is "ProgramFilesFolder" or "ProgramFiles64Folder" or "ProgramFiles6432Folder")
            {
                // Look for a child Directory element
                var child = stdDir.Element(ns + "Directory");
                if (child is not null)
                    return child.Attribute("Name")?.Value;
            }
        }

        return null;
    }

    private static FileEntry MapFile(XElement fileEl)
    {
        return new FileEntry
        {
            Source = fileEl.Attribute("Source")?.Value ?? fileEl.Attribute("src")?.Value ?? "",
            Vital = !string.Equals(fileEl.Attribute("Vital")?.Value, "no", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static ServiceSection MapService(XElement svcEl)
    {
        var startType = svcEl.Attribute("Start")?.Value ?? "auto";
        var startMode = startType switch
        {
            "auto" => "Automatic",
            "demand" => "Manual",
            "disabled" => "Disabled",
            "boot" => "Automatic",
            "system" => "Automatic",
            _ => "Automatic"
        };

        return new ServiceSection
        {
            Name = svcEl.Attribute("Name")?.Value ?? "",
            DisplayName = svcEl.Attribute("DisplayName")?.Value ?? "",
            Description = svcEl.Attribute("Description")?.Value,
            Executable = svcEl.Attribute("Id")?.Value ?? "",
            StartMode = startMode,
            Account = svcEl.Attribute("Account")?.Value ?? "LocalSystem"
        };
    }

    private static RegistryEntrySection MapRegistryEntry(XElement regKeyEl, XElement regValEl)
    {
        return new RegistryEntrySection
        {
            Root = MapRegistryRoot(regKeyEl.Attribute("Root")?.Value ?? "HKLM"),
            Key = regKeyEl.Attribute("Key")?.Value ?? "",
            ValueName = regValEl.Attribute("Name")?.Value ?? "",
            ValueType = MapRegistryType(regValEl.Attribute("Type")?.Value ?? "string"),
            Value = regValEl.Attribute("Value")?.Value ?? ""
        };
    }

    private static RegistryEntrySection MapRegistryValueDirect(XElement regValEl)
    {
        return new RegistryEntrySection
        {
            Root = MapRegistryRoot(regValEl.Attribute("Root")?.Value ?? "HKLM"),
            Key = regValEl.Attribute("Key")?.Value ?? "",
            ValueName = regValEl.Attribute("Name")?.Value ?? "",
            ValueType = MapRegistryType(regValEl.Attribute("Type")?.Value ?? "string"),
            Value = regValEl.Attribute("Value")?.Value ?? ""
        };
    }

    private static string MapRegistryRoot(string wixRoot) => wixRoot.ToUpperInvariant() switch
    {
        "HKLM" => "LocalMachine",
        "HKCU" => "CurrentUser",
        "HKCR" => "ClassesRoot",
        "HKU" => "Users",
        _ => "LocalMachine"
    };

    private static string MapRegistryType(string wixType) => wixType.ToLowerInvariant() switch
    {
        "string" => "String",
        "integer" => "DWord",
        "binary" => "Binary",
        "expandable" => "ExpandString",
        "multistring" => "MultiString",
        _ => "String"
    };

    private static ShortcutSection MapShortcut(XElement shortcutEl)
    {
        var dirId = shortcutEl.Attribute("Directory")?.Value ?? "";
        var isDesktop = dirId.Contains("Desktop", StringComparison.OrdinalIgnoreCase);
        var isStartMenu = dirId.Contains("StartMenu", StringComparison.OrdinalIgnoreCase) ||
                          dirId.Contains("ProgramMenu", StringComparison.OrdinalIgnoreCase) ||
                          dirId.Contains("ApplicationPrograms", StringComparison.OrdinalIgnoreCase);

        return new ShortcutSection
        {
            Name = shortcutEl.Attribute("Name")?.Value ?? "",
            TargetFile = shortcutEl.Attribute("Target")?.Value ?? "",
            Desktop = isDesktop,
            StartMenu = isStartMenu || !isDesktop,
            Description = shortcutEl.Attribute("Description")?.Value,
            Arguments = shortcutEl.Attribute("Arguments")?.Value,
            WorkingDirectory = shortcutEl.Attribute("WorkingDirectory")?.Value,
            IconFile = shortcutEl.Attribute("Icon")?.Value
        };
    }

    private static EnvironmentVariableSection MapEnvironment(XElement envEl)
    {
        var action = envEl.Attribute("Action")?.Value ?? "set";
        var system = envEl.Attribute("System")?.Value;

        return new EnvironmentVariableSection
        {
            Name = envEl.Attribute("Name")?.Value ?? "",
            Value = envEl.Attribute("Value")?.Value ?? "",
            Action = action.Equals("set", StringComparison.OrdinalIgnoreCase) ? "Set" :
                     action.Equals("create", StringComparison.OrdinalIgnoreCase) ? "Set" :
                     "Remove",
            IsSystem = !string.Equals(system, "no", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static FeatureSection MapFeature(
        XElement featureEl,
        XNamespace ns,
        Dictionary<string, List<FileEntry>> componentFiles)
    {
        var feature = new FeatureSection
        {
            Id = featureEl.Attribute("Id")?.Value ?? "",
            Title = featureEl.Attribute("Title")?.Value ?? "",
            Description = featureEl.Attribute("Description")?.Value
        };

        var level = featureEl.Attribute("Level")?.Value;
        if (level is not null && int.TryParse(level, out var installLevel))
        {
            feature.InstallLevel = installLevel;
            feature.IsDefault = installLevel <= 1;
        }

        var absent = featureEl.Attribute("Absent")?.Value;
        if (absent is not null && absent.Equals("disallow", StringComparison.OrdinalIgnoreCase))
            feature.IsRequired = true;

        // Collect files from ComponentRef/ComponentGroupRef
        foreach (var compRef in featureEl.Elements(ns + "ComponentRef"))
        {
            var compId = compRef.Attribute("Id")?.Value;
            if (compId is not null && componentFiles.TryGetValue(compId, out var files))
            {
                feature.Files.AddRange(files);
            }
        }

        // Collect files from inline Component elements
        foreach (var comp in featureEl.Elements(ns + "Component"))
        {
            var compId = comp.Attribute("Id")?.Value ?? "";
            if (componentFiles.TryGetValue(compId, out var files))
            {
                feature.Files.AddRange(files);
            }
        }

        // Map child features
        var childFeatures = featureEl.Elements(ns + "Feature").ToList();
        if (childFeatures.Count > 0)
        {
            feature.Features = [];
            foreach (var childEl in childFeatures)
            {
                feature.Features.Add(MapFeature(childEl, ns, componentFiles));
            }
        }

        return feature;
    }

    private static CustomActionSection MapCustomAction(XElement caEl)
    {
        var ca = new CustomActionSection
        {
            Id = caEl.Attribute("Id")?.Value ?? "",
            Source = caEl.Attribute("BinaryKey")?.Value ?? caEl.Attribute("Property")?.Value ?? "",
            Target = caEl.Attribute("DllEntry")?.Value ?? caEl.Attribute("ExeCommand")?.Value ??
                     caEl.Attribute("Value")?.Value ?? caEl.Attribute("JScriptCall")?.Value ??
                     caEl.Attribute("VBScriptCall")?.Value
        };

        // Determine type from attributes
        if (caEl.Attribute("BinaryKey") is not null && caEl.Attribute("DllEntry") is not null)
            ca.Type = "DllFromBinary";
        else if (caEl.Attribute("BinaryKey") is not null && caEl.Attribute("ExeCommand") is not null)
            ca.Type = "ExeFromBinary";
        else if (caEl.Attribute("Property") is not null && caEl.Attribute("Value") is not null)
            ca.Type = "SetProperty";
        else if (caEl.Attribute("Directory") is not null && caEl.Attribute("ExeCommand") is not null)
            ca.Type = "ExeFromDirectory";

        // Execution attributes
        var execute = caEl.Attribute("Execute")?.Value;
        if (execute is not null)
        {
            ca.Deferred = execute.Equals("deferred", StringComparison.OrdinalIgnoreCase);
            ca.Rollback = execute.Equals("rollback", StringComparison.OrdinalIgnoreCase);
            ca.Commit = execute.Equals("commit", StringComparison.OrdinalIgnoreCase);
        }

        var impersonate = caEl.Attribute("Impersonate")?.Value;
        if (impersonate is not null)
            ca.NoImpersonate = impersonate.Equals("no", StringComparison.OrdinalIgnoreCase);

        var returnAttr = caEl.Attribute("Return")?.Value;
        if (returnAttr is not null)
            ca.ContinueOnError = returnAttr.Equals("ignore", StringComparison.OrdinalIgnoreCase);

        return ca;
    }
}
