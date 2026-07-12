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
    .AutoStart(true)
    // Virtual directories are genuinely created (and removed on uninstall) at install time via
    // Microsoft.Web.Administration — unlike sub-applications, which are still authored-only (IIS014).
    // The physical directory need not exist at compile time (IIS does not validate it); a real
    // deployment would ensure "reports" is populated under INSTALLDIR, e.g. via package.Files(...).
    .VirtualDirectory(vdir => vdir
        .Id("DemoReports")
        .Alias("/reports")
        .Directory("[INSTALLDIR]reports")));

Console.WriteLine($"IIS: {iis.AppPools.Count} pool(s), {iis.WebSites.Count} site(s) " +
    $"({iis.WebSites.Sum(s => s.VirtualDirectories.Count)} virtual director(y/ies)). " +
    "Validation runs automatically during compilation.");

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