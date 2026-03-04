# Demo 33: Util Extension (XML Config Transforms)

Transforms XML configuration files at install time. This demo modifies an `app.config` file by setting an application
setting to `production` mode using an XPath expression, without requiring custom actions or external scripts.

## What This Demonstrates

- Creating a `UtilExtension` instance for general-purpose install utilities
- Using `XmlConfigBuilder` to define XML file transformations
- Targeting specific XML nodes with XPath expressions
- Setting attribute values on matched elements
- Sequencing multiple transforms with `Sequence()`
- `Result<T>` pattern for error handling on the builder

## Key API Calls

```csharp
var util = new UtilExtension();

var config = new XmlConfigBuilder()
    .Id("SetMode")
    .File("[INSTALLDIR]app.config")
    .XPath("//appSettings/add[@key='Mode']")
    .SetAttribute("value", "production")
    .Sequence(1)
    .Build();

util.XmlConfig.Add(config.Value);
```

## How to Build

```shell
dotnet build demo/33-ext-util/33-ext-util.csproj
```

## Notes

- The `File` path uses the `[INSTALLDIR]` MSI property, which resolves to the actual installation directory at runtime.
- XPath expressions follow standard XPath 1.0 syntax. The expression `//appSettings/add[@key='Mode']` targets the
  `<add>` element with `key="Mode"` inside `<appSettings>`.
- `SetAttribute` modifies an existing attribute value. The original file is restored on uninstall.
- Use `Sequence()` to control the order when multiple XML transforms target the same file.
- In production, extensions register automatically via the FalkForge SDK extension pipeline during compilation.
