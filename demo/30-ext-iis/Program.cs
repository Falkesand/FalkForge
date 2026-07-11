using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Extensions.Iis;
using FalkForge.Extensions.Iis.Models;

// Configure an IIS application pool and web site.
var iis = new IisExtension();

var appPool = iis.DefineAppPool(pool => pool
    .Id("DemoPool")
    .Name("DemoPool")
    .NoManagedCode()
    .PipelineMode(ManagedPipelineMode.Integrated)
    .Identity(AppPoolIdentityType.ApplicationPoolIdentity));

iis.AddWebSite(site => site
    .Id("DemoSite")
    .Description("Demo Web Site")
    .Directory("[INSTALLDIR]wwwroot")
    .AppPool(appPool)
    .Binding(8080)
    .AutoStart(true));

Console.WriteLine($"IIS: {iis.AppPools.Count} pool(s), {iis.WebSites.Count} site(s). Validation runs automatically during compilation.");

// Attach the extension to the compiler with .Use(...). This emits the IIsAppPool
// and IIsWebSite configuration tables (plus a placeholder configure CustomAction)
// into the compiled MSI.
return Installer.Build(args, package =>
{
    package.Name = "IIS Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.Files(files => files
        .Add("payload/webapp.dll")
        .To(KnownFolder.ProgramFiles / "Demo" / "IisDemo" / "wwwroot"));
}, new MsiCompiler().Use(iis));