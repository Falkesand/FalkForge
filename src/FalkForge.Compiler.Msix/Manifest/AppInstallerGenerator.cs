using System.Xml.Linq;

namespace FalkForge.Compiler.Msix.Manifest;

public static class AppInstallerGenerator
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/appx/appinstaller/2021";

    public static Result<XDocument> Generate(MsixModel model, string msixFileName)
    {
        if (model.UpdateSettings is null)
            return Result<XDocument>.Failure(ErrorKind.InvalidConfiguration, "UpdateSettings is required for .appinstaller generation.");

        var settings = model.UpdateSettings;

        var doc = new XDocument(
            new XElement(Ns + "AppInstaller",
                new XAttribute("Version", model.Version.ToString()),
                new XAttribute("Uri", settings.AppInstallerUri),
                new XElement(Ns + "MainPackage",
                    new XAttribute("Name", model.Name),
                    new XAttribute("Publisher", model.Publisher),
                    new XAttribute("Version", model.Version.ToString()),
                    new XAttribute("ProcessorArchitecture", MapArchitecture(model.Architecture)),
                    new XAttribute("Uri", GetPackageUri(settings.AppInstallerUri, msixFileName))),
                GenerateUpdateSettings(settings)
            )
        );
        return Result<XDocument>.Success(doc);
    }

    private static XElement GenerateUpdateSettings(MsixUpdateSettings settings)
    {
        var updateSettings = new XElement(Ns + "UpdateSettings");

        var onLaunch = new XElement(Ns + "OnLaunch",
            new XAttribute("HoursBetweenUpdateChecks", settings.HoursBetweenUpdateChecks));
        if (settings.ShowPrompt)
            onLaunch.Add(new XAttribute("ShowPrompt", "true"));
        if (settings.UpdateBlocksActivation)
            onLaunch.Add(new XAttribute("UpdateBlocksActivation", "true"));
        updateSettings.Add(onLaunch);

        if (settings.AutomaticBackgroundTask)
            updateSettings.Add(new XElement(Ns + "AutomaticBackgroundTask"));

        if (settings.ForceUpdateFromAnyVersion)
            updateSettings.Add(new XElement(Ns + "ForceUpdateFromAnyVersion", "true"));

        return updateSettings;
    }

    private static string GetPackageUri(string appInstallerUri, string msixFileName)
    {
        var baseUri = appInstallerUri;
        var lastSlash = baseUri.LastIndexOf('/');
        if (lastSlash >= 0)
            baseUri = baseUri[..(lastSlash + 1)];
        else
            baseUri += "/";
        return baseUri + msixFileName;
    }

    private static string MapArchitecture(ProcessorArchitecture arch) => arch switch
    {
        ProcessorArchitecture.X86 => "x86",
        ProcessorArchitecture.X64 => "x64",
        ProcessorArchitecture.Arm64 => "arm64",
        _ => "x64"
    };
}
