using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Models;
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
    .Binding(8080, "http")
    .AutoStart(true));

var validation = iis.Validate();
if (validation.IsFailure)
{
    Console.Error.WriteLine(validation.Error);
    return 1;
}

Console.WriteLine($"IIS: {iis.AppPools.Count} pool(s), {iis.WebSites.Count} site(s).");

return Installer.Build(args, package =>
{
    package.Name = "IIS Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.Files(files => files
        .Add("payload/webapp.dll")
        .To(KnownFolder.ProgramFiles / "Demo" / "IisDemo" / "wwwroot"));

}, new MsiCompiler());
