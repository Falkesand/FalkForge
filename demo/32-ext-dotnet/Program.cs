using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Models;
using FalkForge.Extensions.DotNet;

// Detect whether .NET 8.0+ runtime is installed.
var dotnet = new DotNetExtension();

var search = new DotNetCoreSearchBuilder()
    .RuntimeType(DotNetRuntimeType.Runtime)
    .Platform(DotNetPlatform.X64)
    .MinVersion(new Version(8, 0, 0))
    .Variable("DOTNET8_FOUND")
    .Build();

if (search.IsFailure)
{
    Console.Error.WriteLine(search.Error);
    return 1;
}

Console.WriteLine($".NET Detection: search for {search.Value.RuntimeType} >= {search.Value.MinimumVersion}");

return Installer.Build(args, package =>
{
    package.Name = ".NET Detection Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.Files(files => files
        .Add("payload/app.exe")
        .To(KnownFolder.ProgramFiles / "Demo" / "DotNetDemo"));

}, new MsiCompiler());
