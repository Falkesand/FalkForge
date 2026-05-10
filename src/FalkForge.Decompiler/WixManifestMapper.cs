using System.Xml.Linq;
using FalkForge.Compiler.Bundle;

namespace FalkForge.Decompiler;

internal static class WixManifestMapper
{
    private static readonly XNamespace NsV3 = "http://schemas.microsoft.com/wix/2008/Burn";
    private static readonly XNamespace NsV4 = "http://wixtoolset.org/schemas/v4/2008/Burn";

    private static XNamespace DetectNamespace(XElement root)
    {
        var ns = root.Name.Namespace;
        if (ns == NsV4)
            return NsV4;

        return NsV3;
    }

    public static Result<(BundleModel Model, IReadOnlyList<WixUnmappedFeature> UnmappedFeatures)> Map(
        XDocument manifest, Guid bundleId)
    {
        var root = manifest.Root;
        if (root is null)
            return Result<(BundleModel, IReadOnlyList<WixUnmappedFeature>)>.Failure(
                ErrorKind.BundleError, "WMM001: Manifest XML has no root element.");

        var ns = DetectNamespace(root);
        var unmapped = new List<WixUnmappedFeature>();

        var (name, manufacturer, version, upgradeCode, scope) = ParseRegistration(root, ns);
        var relatedBundles = ParseRelatedBundles(root, ns);
        var (packages, chainItems) = ParseChain(root, ns, unmapped);
        var containers = ParseContainers(root, ns);
        var variables = ParseVariables(root, ns);

        CollectUnmappedElements(root, ns, unmapped);

        var model = new BundleModel
        {
            Name = name,
            Manufacturer = manufacturer,
            Version = version,
            BundleId = bundleId,
            UpgradeCode = upgradeCode,
            Scope = scope,
            Packages = packages,
            RelatedBundles = relatedBundles,
            Chain = chainItems,
            Containers = containers,
            Variables = variables
        };

        return Result<(BundleModel, IReadOnlyList<WixUnmappedFeature>)>.Success((model, unmapped));
    }

    private static (string Name, string Manufacturer, string Version, Guid UpgradeCode, InstallScope Scope)
        ParseRegistration(XElement root, XNamespace ns)
    {
        var reg = root.Element(ns + "Registration");
        if (reg is null)
            return ("", "", "1.0.0", Guid.Empty, InstallScope.PerMachine);

        var arp = reg.Element(ns + "Arp");

        var name = arp?.Attribute("DisplayName")?.Value
                   ?? reg.Attribute("DisplayName")?.Value
                   ?? "";

        var manufacturer = arp?.Attribute("Publisher")?.Value ?? "";

        var version = reg.Attribute("Version")?.Value ?? "1.0.0";

        var upgradeCode = Guid.Empty;
        var codeAttr = reg.Attribute("Code")?.Value;
        if (codeAttr is not null)
        {
            var trimmed = codeAttr.Trim('{', '}');
            Guid.TryParse(trimmed, out upgradeCode);
        }

        var scope = ParseScope(reg);

        return (name, manufacturer, version, upgradeCode, scope);
    }

    private static InstallScope ParseScope(XElement registration)
    {
        // WiX v3/v4 "Scope" attribute
        var scopeAttr = registration.Attribute("Scope")?.Value;
        if (scopeAttr is not null)
        {
            return scopeAttr switch
            {
                "perUser" => InstallScope.PerUser,
                _ => InstallScope.PerMachine
            };
        }

        // WiX v4 "PerMachine" attribute (yes/no)
        var perMachineAttr = registration.Attribute("PerMachine")?.Value;
        if (perMachineAttr is not null)
        {
            return perMachineAttr switch
            {
                "no" => InstallScope.PerUser,
                _ => InstallScope.PerMachine
            };
        }

        return InstallScope.PerMachine;
    }

    private static List<RelatedBundleModel> ParseRelatedBundles(XElement root, XNamespace ns)
    {
        var result = new List<RelatedBundleModel>();

        foreach (var el in root.Elements(ns + "RelatedBundle"))
        {
            var code = el.Attribute("Code")?.Value;
            if (code is null)
                continue;

            var relation = el.Attribute("Action")?.Value switch
            {
                "Detect" => RelatedBundleRelation.Detect,
                "Upgrade" => RelatedBundleRelation.Upgrade,
                "Addon" => RelatedBundleRelation.Addon,
                "Patch" => RelatedBundleRelation.Patch,
                _ => RelatedBundleRelation.Detect
            };

            result.Add(new RelatedBundleModel
            {
                BundleId = code,
                Relation = relation
            });
        }

        return result;
    }

    private static (List<BundlePackageModel> Packages, List<ChainItem> ChainItems)
        ParseChain(XElement root, XNamespace ns, List<WixUnmappedFeature> unmapped)
    {
        var packages = new List<BundlePackageModel>();
        var chainItems = new List<ChainItem>();

        var chainElement = root.Element(ns + "Chain");
        if (chainElement is null)
            return (packages, chainItems);

        foreach (var child in chainElement.Elements())
        {
            var localName = child.Name.LocalName;

            switch (localName)
            {
                case "MsiPackage":
                case "ExePackage":
                case "MspPackage":
                case "MsuPackage":
                {
                    var pkg = ParsePackageElement(child, localName, ns);
                    packages.Add(pkg);
                    chainItems.Add(new PackageChainItem(pkg));
                    break;
                }
                case "RollbackBoundary":
                {
                    var id = child.Attribute("Id")?.Value ?? $"rb_{chainItems.Count}";
                    var vital = ParseYesNo(child.Attribute("Vital")?.Value, defaultValue: true);
                    chainItems.Add(new RollbackBoundaryChainItem(new RollbackBoundaryModel
                    {
                        Id = id,
                        Vital = vital
                    }));
                    break;
                }
            }
        }

        return (packages, chainItems);
    }

    private static BundlePackageModel ParsePackageElement(
        XElement element, string localName, XNamespace ns)
    {
        var id = element.Attribute("Id")?.Value ?? "";
        var displayName = element.Attribute("DisplayName")?.Value ?? id;
        var version = element.Attribute("Version")?.Value;
        var vital = ParseYesNo(element.Attribute("Vital")?.Value, defaultValue: true);
        var installCondition = element.Attribute("InstallCondition")?.Value;
        var patchCode = element.Attribute("PatchCode")?.Value;
        var containerId = element.Attribute("ContainerId")?.Value;

        var type = localName switch
        {
            "MsiPackage" => BundlePackageType.MsiPackage,
            "ExePackage" => BundlePackageType.ExePackage,
            "MspPackage" => BundlePackageType.MspPackage,
            "MsuPackage" => BundlePackageType.MsuPackage,
            _ => BundlePackageType.ExePackage
        };

        var properties = new Dictionary<string, string>();
        foreach (var child in element.Elements(ns + "MsiProperty"))
        {
            var propId = child.Attribute("Id")?.Value;
            var propValue = child.Attribute("Value")?.Value ?? "";
            if (propId is not null)
                properties[propId] = propValue;
        }

        var exitCodes = new Dictionary<int, ExitCodeBehavior>();
        foreach (var child in element.Elements(ns + "ExitCode"))
        {
            var codeAttr = child.Attribute("Code")?.Value;
            if (codeAttr is not null && int.TryParse(codeAttr, out var code))
            {
                var behavior = child.Attribute("Type")?.Value switch
                {
                    "1" => ExitCodeBehavior.Success,
                    "3" => ExitCodeBehavior.RebootRequired,
                    "4" => ExitCodeBehavior.ScheduleReboot,
                    _ => ExitCodeBehavior.Failure
                };
                exitCodes[code] = behavior;
            }
        }

        return new BundlePackageModel
        {
            Id = id,
            Type = type,
            DisplayName = displayName,
            Version = version,
            Vital = vital,
            SourcePath = element.Attribute("SourceFile")?.Value ?? "",
            InstallCondition = installCondition,
            PatchCode = patchCode,
            ContainerId = containerId,
            Properties = properties,
            ExitCodes = exitCodes
        };
    }

    private static List<ContainerModel> ParseContainers(XElement root, XNamespace ns)
    {
        var result = new List<ContainerModel>();

        foreach (var el in root.Elements(ns + "Container"))
        {
            var id = el.Attribute("Id")?.Value;
            if (id is null)
                continue;

            var downloadUrl = el.Attribute("DownloadUrl")?.Value;

            result.Add(new ContainerModel
            {
                Id = id,
                DownloadUrl = downloadUrl
            });
        }

        return result;
    }

    private static List<BundleVariableModel> ParseVariables(XElement root, XNamespace ns)
    {
        var result = new List<BundleVariableModel>();

        foreach (var el in root.Elements(ns + "Variable"))
        {
            var name = el.Attribute("Id")?.Value ?? el.Attribute("Name")?.Value ?? "";
            if (string.IsNullOrEmpty(name))
                continue;

            var type = el.Attribute("Type")?.Value switch
            {
                "numeric" => BundleVariableType.Numeric,
                "version" => BundleVariableType.Version,
                _ => BundleVariableType.String
            };

            var defaultValue = el.Attribute("Value")?.Value;
            var hidden = ParseYesNo(el.Attribute("Hidden")?.Value, defaultValue: false);
            var persisted = ParseYesNo(el.Attribute("Persisted")?.Value, defaultValue: false);

            result.Add(new BundleVariableModel(name, type, defaultValue, persisted, hidden, Secret: false));
        }

        return result;
    }

    private static void CollectUnmappedElements(
        XElement root, XNamespace ns, List<WixUnmappedFeature> unmapped)
    {
        foreach (var el in root.Elements(ns + "Search"))
        {
            var id = el.Attribute("Id")?.Value ?? "";
            var variable = el.Attribute("Variable")?.Value ?? "";
            unmapped.Add(new WixUnmappedFeature(
                "Search",
                $"Id={id} Variable={variable}",
                el.ToString()));
        }

        var ux = root.Element(ns + "UX");
        if (ux is not null)
        {
            unmapped.Add(new WixUnmappedFeature(
                "BootstrapperApplication",
                $"UX element with {ux.Elements().Count()} children",
                ux.ToString()));
        }

        foreach (var el in root.Elements(ns + "ApprovedExeForElevation"))
        {
            var id = el.Attribute("Id")?.Value ?? "";
            unmapped.Add(new WixUnmappedFeature(
                "ApprovedExeForElevation",
                $"Id={id}",
                el.ToString()));
        }

        foreach (var el in root.Elements(ns + "BootstrapperExtension"))
        {
            var id = el.Attribute("Id")?.Value ?? "";
            unmapped.Add(new WixUnmappedFeature(
                "BootstrapperExtension",
                $"Id={id}",
                el.ToString()));
        }
    }

    private static bool ParseYesNo(string? value, bool defaultValue) =>
        value switch
        {
            "yes" => true,
            "no" => false,
            _ => defaultValue
        };
}
