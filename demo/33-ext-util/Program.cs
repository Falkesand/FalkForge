using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Models;
using FalkForge.Extensions.Util;
using FalkForge.Extensions.Util.XmlConfig;

// Transform XML config files and run a silent post-install command.
var util = new UtilExtension();

// Modify app.config at install time
var config = new XmlConfigBuilder()
    .Id("SetMode")
    .File("[INSTALLDIR]app.config")
    .XPath("//appSettings/add[@key='Mode']")
    .SetAttribute("value", "production")
    .Sequence(1)
    .Build();

if (config.IsFailure)
{
    Console.Error.WriteLine(config.Error);
    return 1;
}

util.XmlConfig.Add(config.Value);
Console.WriteLine($"Util: 1 XmlConfig entry configured.");

return Installer.Build(args, package =>
{
    package.Name = "Util Extension Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.Files(files => files
        .Add("payload/app.exe")
        .Add("payload/app.config")
        .To(KnownFolder.ProgramFiles / "Demo" / "UtilDemo"));

}, new MsiCompiler());
