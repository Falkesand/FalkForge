# FalkForge Plugin System Design

## Overview
General-purpose plugin system for FalkForge that enables reusable services accessible from UI pages, Engine, and CLI. Plugins register typed services into a frozen registry. Three shipped plugins demonstrate the pattern: SQL, ODBC, and FileSystem.

## Security Model
- **Compile-time only**: No dynamic loading from disk, MEF, or assembly scanning. Plugins are ProjectReference dependencies explicitly registered in Program.cs.
- **Registry freezing**: Registrations accepted only during startup. Frozen after initialization. Late registration throws InvalidOperationException.
- **Read-only consumer view**: Pages resolve via IPluginServices (read-only). Cannot register, replace, or remove services.
- **First-registration-wins**: Duplicate service type registrations are silently ignored. FalkForge-shipped plugins register first.

## Core Interfaces (in FalkForge.Core)

### IInstallerPlugin
```csharp
public interface IInstallerPlugin
{
    string Name { get; }
    void RegisterServices(IPluginServiceRegistry registry);
}
```

### IPluginServiceRegistry
```csharp
public interface IPluginServiceRegistry
{
    void Register<TService>(TService instance) where TService : class;
    void Register<TService>(Func<TService> factory) where TService : class;
}
```

### IPluginServices
```csharp
public interface IPluginServices
{
    TService? GetService<TService>() where TService : class;
    TService GetRequiredService<TService>() where TService : class;
}
```

### PluginServiceRegistry (internal implementation)
```csharp
internal sealed class PluginServiceRegistry : IPluginServiceRegistry, IPluginServices
{
    private readonly Dictionary<Type, Func<object>> _factories = new();
    private bool _frozen;

    public void Register<T>(T instance) where T : class
    {
        if (_frozen) throw new InvalidOperationException("Plugin registry is frozen.");
        _factories.TryAdd(typeof(T), () => instance);
    }

    public void Register<T>(Func<T> factory) where T : class
    {
        if (_frozen) throw new InvalidOperationException("Plugin registry is frozen.");
        _factories.TryAdd(typeof(T), () => factory());
    }

    public void Freeze() => _frozen = true;

    public T? GetService<T>() where T : class
        => _factories.TryGetValue(typeof(T), out var factory) ? (T)factory() : null;

    public T GetRequiredService<T>() where T : class
        => GetService<T>() ?? throw new InvalidOperationException($"No service registered for {typeof(T).Name}.");
}
```

## InstallerPage Integration

InstallerPage gets a new property:
```csharp
public IPluginServices PluginServices { get; internal set; } = null!;
```

Set by InstallerApp.RunCore alongside Engine and SharedState.

InstallerUIBuilder gets:
```csharp
public InstallerUIBuilder Plugin<T>() where T : IInstallerPlugin, new();
```

## Shipped Plugins

### Plugin 1: FalkForge.Plugins.Sql
**Project**: `src/FalkForge.Plugins.Sql/`
**Dependencies**: FalkForge.Core, Microsoft.Data.SqlClient

Service interfaces (defined in the plugin project):
- `ISqlServerDiscovery` — Discover SQL Server instances via UDP broadcast + registry
- `IDatabaseLister` — List databases on a SQL Server instance
- `IConnectionTester` — Test SQL connection with credentials

### Plugin 2: FalkForge.Plugins.Odbc
**Project**: `src/FalkForge.Plugins.Odbc/`
**Dependencies**: FalkForge.Core (P/Invoke to odbccp32.dll)

Service interfaces:
- `IOdbcManager` — Check DSN existence, launch ODBC administrator

### Plugin 3: FalkForge.Plugins.FileSystem
**Project**: `src/FalkForge.Plugins.FileSystem/`
**Dependencies**: FalkForge.Core (WPF interop for folder dialog)

Service interfaces:
- `IFolderBrowser` — Show folder browse dialog

## Dependency Graph Addition
```
Core (no deps)
  +-> Plugins.Sql (Core + Microsoft.Data.SqlClient)
  +-> Plugins.Odbc (Core, P/Invoke)
  +-> Plugins.FileSystem (Core, WPF interop)
```

## MAS Demo Integration
```csharp
InstallerApp.Run(args, app => app
    .Plugin<SqlPlugin>()
    .Plugin<OdbcPlugin>()
    .Plugin<FileSystemPlugin>()
    .Window(w => w.CustomWindow<MasInstallerWindow>())
    .Pages(p => p...));
```

Pages use services:
```csharp
var discovery = PluginServices.GetService<ISqlServerDiscovery>();
var servers = await discovery.DiscoverServersAsync();
```
