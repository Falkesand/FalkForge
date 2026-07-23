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

// Register the search so the extension emits real MSI-native detection (Signature + DrLocator +
// AppSearch — the built-in AppSearch standard action evaluates these natively at install time, no
// custom action required) into the compiled MSI's own tables.
var addResult = dotnet.AddSearch(search.Value);
if (addResult.IsFailure)
{
    Console.Error.WriteLine(addResult.Error);
    return 1;
}

Console.WriteLine($".NET Detection: search for {search.Value.RuntimeType} >= {search.Value.MinimumVersion}");

return Installer.Build(args, package =>
{
    package.Name = ".NET Detection Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    // The search model above carries no Message, so the extension emits no LaunchCondition of its
    // own (see DotNetLaunchConditionContributor) — this is the C# fluent authoring path's shape: the
    // author gates explicitly on the AppSearch-populated property.
    package.Require("DOTNET8_FOUND",
        ".NET 8.0 Runtime (x64) or later is required. Please install it from https://dotnet.microsoft.com/download");

    package.Files(files => files
        .Add("payload/app.exe")
        .To(KnownFolder.ProgramFiles / "Demo" / "DotNetDemo"));
    // Attach the extension with .Use(...). The extension contributes Signature/DrLocator/AppSearch
    // to populate DOTNET8_FOUND — it emits no LaunchCondition of its own here; the actual install
    // gate is the package.Require(...) call above, not the extension.
}, new MsiCompiler().Use(dotnet));