using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Extensions.Util;
using FalkForge.Extensions.Util.XmlConfig;

// Transform XML config files, run a silent post-install command, share a data folder, drop an
// internet shortcut, and clean up a cache folder on uninstall.
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
Console.WriteLine("Util: 1 XmlConfig entry configured.");

// Run a silent post-install command (deferred, elevated custom action).
var quietExec = util.AddQuietExec(q => q
    .Id("Warmup")
    .Command("[INSTALLDIR]app.exe --warmup")
    .WorkingDir("[INSTALLDIR]"));

if (quietExec.IsFailure)
{
    Console.Error.WriteLine(quietExec.Error);
    return 1;
}

// Share the app's data folder over SMB.
var fileShare = util.AddFileShare(f => f
    .Id("AppData")
    .Name("UtilDemoAppData")
    .Description("Util extension demo data share")
    .Directory(@"C:\ProgramData\FalkForge\UtilDemo")
    .GrantRead("Everyone"));

if (fileShare.IsFailure)
{
    Console.Error.WriteLine(fileShare.Error);
    return 1;
}

// Drop a .url shortcut to the product's home page.
var internetShortcut = util.AddInternetShortcut(s => s
    .Id("Home")
    .Name("Util Demo Home")
    .Target("https://example.com/util-demo")
    .Directory("[INSTALLDIR]"));

if (internetShortcut.IsFailure)
{
    Console.Error.WriteLine(internetShortcut.Error);
    return 1;
}

// Remove a leftover cache folder on uninstall.
var removeFolderEx = util.AddRemoveFolderEx(r => r
    .Id("Cache")
    .Directory(@"C:\ProgramData\FalkForge\UtilDemo\Cache")
    .OnUninstall());

if (removeFolderEx.IsFailure)
{
    Console.Error.WriteLine(removeFolderEx.Error);
    return 1;
}

Console.WriteLine("Util: QuietExec, FileShare, InternetShortcut and RemoveFolderEx configured.");

// Attach the extension to the compiler with .Use(...). This emits the XmlConfig table + row AND
// schedules the deferred, elevated custom actions that make the other four features run for real.
return Installer.Build(args, package =>
{
    package.Name = "Util Extension Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.Files(files => files
        .Add("payload/app.exe")
        .Add("payload/app.config")
        .To(KnownFolder.ProgramFiles / "Demo" / "UtilDemo"));
}, new MsiCompiler().Use(util));