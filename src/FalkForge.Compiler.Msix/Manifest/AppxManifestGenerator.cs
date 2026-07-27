using System.Xml.Linq;

namespace FalkForge.Compiler.Msix.Manifest;

public static class AppxManifestGenerator
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private static readonly XNamespace Uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
    private static readonly XNamespace Uap10 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/10";
    private static readonly XNamespace Rescap = "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities";
    private static readonly XNamespace Desktop = "http://schemas.microsoft.com/appx/manifest/desktop/windows10";

    public static Result<XDocument> Generate(MsixModel model)
    {
        var doc = new XDocument(
            new XElement(Ns + "Package",
                new XAttribute(XNamespace.Xmlns + "uap", Uap),
                new XAttribute(XNamespace.Xmlns + "uap10", Uap10),
                new XAttribute(XNamespace.Xmlns + "rescap", Rescap),
                new XAttribute(XNamespace.Xmlns + "desktop", Desktop),
                new XAttribute("IgnorableNamespaces", "uap uap10 rescap desktop"),
                GenerateIdentity(model),
                GenerateProperties(model),
                GenerateDependencies(model),
                GenerateCapabilities(model),
                GenerateApplications(model)
            )
        );
        return Result<XDocument>.Success(doc);
    }

    private static XElement GenerateIdentity(MsixModel model)
    {
        return new XElement(Ns + "Identity",
            new XAttribute("Name", model.Name),
            new XAttribute("Publisher", model.Publisher),
            new XAttribute("Version", model.Version.ToString()),
            new XAttribute("ProcessorArchitecture", MapArchitecture(model.Architecture)));
    }

    private static XElement GenerateProperties(MsixModel model)
    {
        var props = new XElement(Ns + "Properties",
            new XElement(Ns + "DisplayName", model.DisplayName),
            new XElement(Ns + "PublisherDisplayName", model.PublisherDisplayName));
        if (model.Description is not null)
            props.Add(new XElement(Ns + "Description", model.Description));
        if (model.LogoPath is not null)
            props.Add(new XElement(Ns + "Logo", model.LogoPath));
        return props;
    }

    private static XElement GenerateDependencies(MsixModel model)
    {
        var deps = new XElement(Ns + "Dependencies",
            new XElement(Ns + "TargetDeviceFamily",
                new XAttribute("Name", "Windows.Desktop"),
                new XAttribute("MinVersion", model.MinWindowsVersion),
                new XAttribute("MaxVersionTested", model.MaxVersionTested ?? "10.0.26100.0")));

        foreach (var dep in model.Dependencies)
        {
            var el = new XElement(Ns + "PackageDependency",
                new XAttribute("Name", dep.Name),
                new XAttribute("Publisher", dep.Publisher));
            if (dep.MinVersion is not null)
                el.Add(new XAttribute("MinVersion", dep.MinVersion.ToString()));
            deps.Add(el);
        }
        return deps;
    }

    private static XElement GenerateCapabilities(MsixModel model)
    {
        var caps = new XElement(Ns + "Capabilities");
        foreach (var cap in model.Capabilities)
            caps.Add(new XElement(Ns + "Capability", new XAttribute("Name", cap)));
        foreach (var cap in model.RestrictedCapabilities)
            caps.Add(new XElement(Rescap + "Capability", new XAttribute("Name", cap)));
        return caps;
    }

    private static XElement GenerateApplications(MsixModel model)
    {
        var apps = new XElement(Ns + "Applications");
        foreach (var app in model.Applications)
        {
            var appEl = new XElement(Ns + "Application",
                new XAttribute("Id", app.Id),
                new XAttribute("Executable", app.Executable));
            if (app.EntryPoint is not null)
                appEl.Add(new XAttribute("EntryPoint", app.EntryPoint));

            appEl.Add(new XElement(Uap + "VisualElements",
                new XAttribute("DisplayName", app.VisualElements.DisplayName),
                new XAttribute("Square150x150Logo", app.VisualElements.Square150x150Logo ?? "Assets\\Square150x150Logo.png"),
                new XAttribute("Square44x44Logo", app.VisualElements.Square44x44Logo ?? "Assets\\Square44x44Logo.png"),
                new XAttribute("BackgroundColor", app.VisualElements.BackgroundColor)));

            // CT_Application sequences uap:VisualElements before Extensions, so this must stay last.
            var extensions = GenerateExtensions(app);
            if (extensions is not null)
                appEl.Add(extensions);

            apps.Add(appEl);
        }
        return apps;
    }

    /// <summary>
    /// Builds the application's <c>Extensions</c> element, or <see langword="null"/> when the
    /// application declares none — the schema requires at least one <c>Extension</c> child, so
    /// an empty container would make the package fail validation.
    /// </summary>
    private static XElement? GenerateExtensions(MsixApplication app)
    {
        if (app.FileTypeAssociations.Count == 0 && app.Protocols.Count == 0)
            return null;

        var extensions = new XElement(Ns + "Extensions");

        foreach (var fta in app.FileTypeAssociations)
        {
            var association = new XElement(Uap + "FileTypeAssociation",
                new XAttribute("Name", fta.Name));

            // CT_FileTypeAssociation sequence: DisplayName, Logo, SupportedFileTypes.
            if (fta.DisplayName is not null)
                association.Add(new XElement(Uap + "DisplayName", fta.DisplayName));
            if (fta.Logo is not null)
                association.Add(new XElement(Uap + "Logo", fta.Logo));

            var supported = new XElement(Uap + "SupportedFileTypes");
            foreach (var fileType in fta.FileTypes)
                supported.Add(new XElement(Uap + "FileType", fileType));
            association.Add(supported);

            extensions.Add(new XElement(Uap + "Extension",
                new XAttribute("Category", "windows.fileTypeAssociation"),
                association));
        }

        foreach (var protocol in app.Protocols)
        {
            var protocolEl = new XElement(Uap + "Protocol",
                new XAttribute("Name", protocol.Name));

            // CT_Protocol sequence: Logo, DisplayName.
            if (protocol.Logo is not null)
                protocolEl.Add(new XElement(Uap + "Logo", protocol.Logo));
            if (protocol.DisplayName is not null)
                protocolEl.Add(new XElement(Uap + "DisplayName", protocol.DisplayName));

            extensions.Add(new XElement(Uap + "Extension",
                new XAttribute("Category", "windows.protocol"),
                protocolEl));
        }

        return extensions;
    }

    private static string MapArchitecture(ProcessorArchitecture arch) => arch switch
    {
        ProcessorArchitecture.X86 => "x86",
        ProcessorArchitecture.X64 => "x64",
        ProcessorArchitecture.Arm64 => "arm64",
        _ => "x64"
    };
}
