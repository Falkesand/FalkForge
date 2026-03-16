# Demo 48: COM Registration

Registers COM classes during MSI installation, including in-process DLL servers and out-of-process EXE servers with
ProgId and threading model configuration.

## What This Demonstrates

- `package.ComClass()` for registering COM classes via the MSI `Class` table
- `InprocServer32()` for DLL-based in-process COM servers
- `LocalServer32()` for EXE-based out-of-process COM servers
- `ThreadingModel()` with `Apartment`, `Both`, `Free`, and `Neutral` options
- `ProgId()` and `Description()` for human-readable COM class identification

## Key API Calls

```csharp
// In-process COM server with Apartment threading
package.ComClass(com => com
    .ClassId(new Guid("B5F8350B-0548-48B1-A6EE-88BD00B4A5E7"))
    .InprocServer32()
    .ThreadingModel(ComThreadingModel.Apartment)
    .ProgId("Demo.DataProcessor")
    .Description("FalkForge Demo Data Processor COM Class"));

// Out-of-process COM server
package.ComClass(com => com
    .ClassId(new Guid("C7D8E9F0-1A2B-3C4D-5E6F-A7B8C9D0E1F2"))
    .LocalServer32()
    .ProgId("Demo.ServiceHost")
    .Description("Out-of-process COM service host"));
```

## How to Build

```bash
dotnet build demo/48-com-registration
```

## Notes

- COM class registration is written to the MSI `Class` table and handled natively by Windows Installer.
- The `ClassId` (CLSID) must be a unique GUID for each COM class.
- `InprocServer32` loads the DLL into the caller's process; `LocalServer32` launches a separate EXE process.
- `Apartment` threading model is the default and suitable for most single-threaded COM components. Use `Both` for
  components that support both apartment-threaded and free-threaded callers.
