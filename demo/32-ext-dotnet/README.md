# Demo 32: .NET Runtime Detection Extension

Detects whether a specific .NET runtime version is installed on the target machine using a factory pattern. This demo searches for the .NET 8.0+ x64 runtime, stores the result in a custom MSI property, and uses it as a launch condition to block installation when the prerequisite is missing.

## What This Demonstrates

- Creating a `DotNetExtension` instance and using its `SearchForRuntime()` factory method
- Specifying runtime type, platform architecture, and minimum version
- Binding the search result to an MSI property variable
- `Result<T>` pattern for error handling on the builder
- Using `package.Require()` as a launch condition to block installation when the prerequisite is missing

## Key API Calls

```csharp
// Factory pattern — create the search via the extension instance
var dotnet = new DotNetExtension();

var search = dotnet.SearchForRuntime()
    .RuntimeType(DotNetRuntimeType.Runtime)
    .Platform(DotNetPlatform.X64)
    .MinVersion(new Version(8, 0, 0))
    .Variable("DOTNET8_FOUND")
    .Build();

// Use the search variable as a launch condition — block install if .NET 8 is missing
package.Require("DOTNET8_FOUND",
    ".NET 8.0 Runtime (x64) or later is required. Please install it from https://dotnet.microsoft.com/download");
```

## How to Build

```shell
dotnet build demo/32-ext-dotnet/32-ext-dotnet.csproj
```

## Notes

- The `SearchForRuntime()` factory method on `DotNetExtension` replaces the standalone `DotNetCoreSearchBuilder` constructor, keeping the search tied to its extension context.
- `RuntimeType` distinguishes between `Runtime` (base), `AspNetCore`, and `WindowsDesktop` runtimes.
- `Variable("DOTNET8_FOUND")` sets an MSI property that can be referenced in launch conditions or UI to block installation when the prerequisite is missing.
- `package.Require()` adds a launch condition. If the named property is not set (i.e., the runtime was not found), the installer displays the error message and exits before any files are installed.
- The search runs during the MSI `AppSearch` phase, before file installation begins.
- In production, extensions register automatically via the FalkForge SDK extension pipeline during compilation.
