using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Extensions.DotNet;

// Detect whether .NET 8.0+ runtime is installed and block install if missing.
var dotnet = new DotNetExtension();

var search = dotnet.SearchForRuntime()
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

    // Use the search variable as a launch condition — block install if .NET 8 is missing
    package.Require("DOTNET8_FOUND",
        ".NET 8.0 Runtime (x64) or later is required. Please install it from https://dotnet.microsoft.com/download");

    package.Files(files => files
        .Add("payload/app.exe")
        .To(KnownFolder.ProgramFiles / "Demo" / "DotNetDemo"));
    // Attach the extension with .Use(...). The .NET extension is detection-only (it
    // contributes no MSI tables); the launch condition above does the gating.
}, new MsiCompiler().Use(dotnet));