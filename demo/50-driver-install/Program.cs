using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Extensions.Driver;

// Driver installation: INF-based device driver with ForceInstall and PlugAndPlay flags.
var driver = new DriverExtension();

// --- Standard device driver ---
// Installs a device driver from an INF file with Plug and Play support.
var result1 = driver.AddDriver(d => d
    .Id("UsbDeviceDriver")
    .InfFilePath("payload/device.inf")
    .PlugAndPlay()
    .Description("Demo USB Device Driver"));

if (result1.IsFailure)
{
    Console.Error.WriteLine($"Driver config error: {result1.Error}");
    return 1;
}

// --- Force-install driver ---
// Forces driver installation even if a newer version is already present.
// Useful for downgrade scenarios or replacing third-party drivers.
var result2 = driver.AddDriver(d => d
    .Id("PrinterDriver")
    .InfFilePath("payload/printer.inf")
    .Force()
    .PlugAndPlay()
    .Description("Demo Printer Driver (force install)")
    .Condition(Condition.Is64BitOS.ToString()));

if (result2.IsFailure)
{
    Console.Error.WriteLine($"Driver config error: {result2.Error}");
    return 1;
}

// Validate all driver configurations
var errors = driver.ValidateDrivers();
if (errors.Count > 0)
{
    foreach (var e in errors)
        Console.Error.WriteLine($"{e.Code}: {e.Message}");
    return 1;
}

Console.WriteLine($"Driver extension: {errors.Count} errors, 2 drivers configured.");

// Attach the extension to the compiler with .Use(...). This emits the FalkDriverPackage
// table + rows into the compiled MSI.
return Installer.Build(args, package =>
{
    package.Name = "Driver Install Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    // NeverOverwrite prevents replacing INF files if they were modified post-install
    package.Files(files => files
        .Add("payload/device.inf")
        .Add("payload/printer.inf")
        .NeverOverwrite()
        .To(KnownFolder.ProgramFiles / "Demo" / "DriverDemo"));
}, new MsiCompiler().Use(driver));
