# Demo 27: GAC Assembly

Registers a .NET assembly in the Global Assembly Cache (GAC), making it available to all .NET applications on the machine.

## What This Demonstrates

- Installing a DLL to a local directory and registering it in the GAC
- Using `package.GacAssembly()` to declare GAC registration
- Specifying the assembly type (`AssemblyType.DotNetAssembly`)

## Key API Calls

```csharp
// Deploy the assembly file
package.Files(files => files
    .Add("payload/MyLib.dll")
    .To(KnownFolder.ProgramFiles / "Demo" / "GacDemo"));

// Register in the GAC
package.GacAssembly(asm => asm
    .FileRef("MyLib.dll")
    .Type(AssemblyType.DotNetAssembly));
```

## How to Build

```bash
dotnet build demo/27-gac-assembly
```

## Notes

- The assembly must be strong-named (signed with a key) to be eligible for GAC registration.
- `FileRef()` references the file by the name used in `Files()`. The file is deployed to the specified directory and also registered in the GAC.
- GAC registration is primarily relevant for .NET Framework assemblies. .NET Core / .NET 5+ applications typically do not use the GAC.
