# Demo 50: Driver Installation

Installs device drivers from INF files during MSI installation using the `DriverExtension` API with Plug and Play
and force-install support.

## What This Demonstrates

- Creating a `DriverExtension` instance and adding driver configurations
- `InfFilePath()` to specify the driver INF file
- `PlugAndPlay()` flag for PnP-aware driver installation
- `Force()` flag to install even when a newer driver version exists
- `Condition()` for conditional driver installation (e.g., 64-bit OS only)
- `Description()` for human-readable driver identification
- Validating all driver configurations with `ValidateDrivers()`

## Key API Calls

```csharp
var driver = new DriverExtension();

// Standard PnP device driver
driver.AddDriver(d => d
    .Id("UsbDeviceDriver")
    .InfFilePath("payload/device.inf")
    .PlugAndPlay()
    .Description("Demo USB Device Driver"));

// Force-install driver with OS condition
driver.AddDriver(d => d
    .Id("PrinterDriver")
    .InfFilePath("payload/printer.inf")
    .Force()
    .PlugAndPlay()
    .Description("Demo Printer Driver (force install)")
    .Condition(Condition.Is64BitOS.ToString()));

var errors = driver.ValidateDrivers();
```

## How to Build

```bash
dotnet build demo/50-driver-install
```

## Notes

- Driver installation uses `pnputil` under the hood, executed as deferred custom actions with elevated privileges.
- The `Force()` flag maps to `DriverInstallFlags.ForceInstall`, which replaces existing drivers even if the installed
  version is newer.
- `PlugAndPlay()` enables PnP-aware installation, allowing Windows to automatically associate the driver with matching
  hardware.
- INF files must end with `.inf` (validated by DRV003).
- Drivers are removed on uninstall via `pnputil /delete-driver`.
