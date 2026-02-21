# FalkForge Plugin System Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a general-purpose plugin system for FalkForge that enables reusable services (SQL discovery, connection testing, ODBC management, folder browsing) accessible from UI pages, then wire the MAS demo to use them.

**Architecture:** Plugins implement `IInstallerPlugin` and register typed services into a frozen `PluginServiceRegistry`. Pages access services via `IPluginServices` property. Three shipped plugins (SQL, ODBC, FileSystem) provide the concrete implementations. Security: compile-time only, first-registration-wins, frozen after init.

**Tech Stack:** .NET 10, C# latest, Microsoft.Data.SqlClient, P/Invoke (odbccp32.dll), WPF interop (CommonOpenFileDialog)

---

## Reference: Existing Patterns

- `Result<T>` at `src/FalkForge.Core/Result.cs` — `Result<T>.Success(value)` / `Result<T>.Failure(error)`
- `Error` at `src/FalkForge.Core/Error.cs` — `record struct Error(ErrorKind Kind, string Message)`
- `ErrorKind` at `src/FalkForge.Core/ErrorKind.cs` — add new kinds as needed
- `Unit` at `src/FalkForge.Core/Unit.cs` — for `Result<Unit>`
- `InstallerPage` at `src/FalkForge.Ui/InstallerPage.cs` — has `Engine`, `SharedState` (internal set)
- `InstallerUIBuilder` at `src/FalkForge.Ui/InstallerUIBuilder.cs` — fluent builder
- `InstallerApp.RunCore` at `src/FalkForge.Ui/InstallerApp.cs` — wires pages
- One class per file. `TreatWarningsAsErrors`. Nullable enabled.

---

### Task 1: Plugin Core Interfaces in FalkForge.Core

**Files:**
- Create: `src/FalkForge.Core/Plugins/IInstallerPlugin.cs`
- Create: `src/FalkForge.Core/Plugins/IPluginServiceRegistry.cs`
- Create: `src/FalkForge.Core/Plugins/IPluginServices.cs`

**Step 1: Create IInstallerPlugin**

```csharp
namespace FalkForge.Plugins;

public interface IInstallerPlugin
{
    string Name { get; }
    void RegisterServices(IPluginServiceRegistry registry);
}
```

**Step 2: Create IPluginServiceRegistry**

```csharp
namespace FalkForge.Plugins;

public interface IPluginServiceRegistry
{
    void Register<TService>(TService instance) where TService : class;
    void Register<TService>(Func<TService> factory) where TService : class;
}
```

**Step 3: Create IPluginServices**

```csharp
namespace FalkForge.Plugins;

public interface IPluginServices
{
    TService? GetService<TService>() where TService : class;
    TService GetRequiredService<TService>() where TService : class;
}
```

**Step 4: Build**

Run: `dotnet build src/FalkForge.Core/FalkForge.Core.csproj`
Expected: 0 warnings, 0 errors.

**Step 5: Commit**

```bash
git add src/FalkForge.Core/Plugins/
git commit -m "feat: add plugin system core interfaces"
```

---

### Task 2: PluginServiceRegistry Implementation + Tests

**Files:**
- Create: `src/FalkForge.Core/Plugins/PluginServiceRegistry.cs`
- Create: `tests/FalkForge.Core.Tests/Plugins/PluginServiceRegistryTests.cs`

**Step 1: Write failing tests**

```csharp
namespace FalkForge.Core.Tests.Plugins;

using FalkForge.Plugins;

public sealed class PluginServiceRegistryTests
{
    [Fact]
    public void Register_instance_and_resolve()
    {
        var registry = new PluginServiceRegistry();
        var service = new FakeService();
        registry.Register<IFakeService>(service);

        IPluginServices services = registry;
        Assert.Same(service, services.GetService<IFakeService>());
    }

    [Fact]
    public void Register_factory_and_resolve()
    {
        var registry = new PluginServiceRegistry();
        registry.Register<IFakeService>(() => new FakeService());

        IPluginServices services = registry;
        Assert.NotNull(services.GetService<IFakeService>());
    }

    [Fact]
    public void GetService_unregistered_returns_null()
    {
        var registry = new PluginServiceRegistry();
        IPluginServices services = registry;
        Assert.Null(services.GetService<IFakeService>());
    }

    [Fact]
    public void GetRequiredService_unregistered_throws()
    {
        var registry = new PluginServiceRegistry();
        IPluginServices services = registry;
        Assert.Throws<InvalidOperationException>(() => services.GetRequiredService<IFakeService>());
    }

    [Fact]
    public void First_registration_wins()
    {
        var registry = new PluginServiceRegistry();
        var first = new FakeService();
        var second = new FakeService();
        registry.Register<IFakeService>(first);
        registry.Register<IFakeService>(second);

        IPluginServices services = registry;
        Assert.Same(first, services.GetService<IFakeService>());
    }

    [Fact]
    public void Freeze_prevents_registration()
    {
        var registry = new PluginServiceRegistry();
        registry.Freeze();
        Assert.Throws<InvalidOperationException>(() => registry.Register<IFakeService>(new FakeService()));
    }

    [Fact]
    public void Freeze_allows_resolve()
    {
        var registry = new PluginServiceRegistry();
        registry.Register<IFakeService>(new FakeService());
        registry.Freeze();

        IPluginServices services = registry;
        Assert.NotNull(services.GetService<IFakeService>());
    }

    private interface IFakeService { }
    private sealed class FakeService : IFakeService { }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/FalkForge.Core.Tests/ --filter "FullyQualifiedName~PluginServiceRegistry"`
Expected: FAIL (PluginServiceRegistry class doesn't exist)

**Step 3: Implement PluginServiceRegistry**

```csharp
namespace FalkForge.Plugins;

public sealed class PluginServiceRegistry : IPluginServiceRegistry, IPluginServices
{
    private readonly Dictionary<Type, Func<object>> _factories = new();
    private bool _frozen;

    public void Register<TService>(TService instance) where TService : class
    {
        if (_frozen) throw new InvalidOperationException("Plugin registry is frozen after initialization.");
        _factories.TryAdd(typeof(TService), () => instance);
    }

    public void Register<TService>(Func<TService> factory) where TService : class
    {
        if (_frozen) throw new InvalidOperationException("Plugin registry is frozen after initialization.");
        _factories.TryAdd(typeof(TService), () => factory());
    }

    public void Freeze() => _frozen = true;

    public TService? GetService<TService>() where TService : class
        => _factories.TryGetValue(typeof(TService), out var factory) ? (TService)factory() : null;

    public TService GetRequiredService<TService>() where TService : class
        => GetService<TService>() ?? throw new InvalidOperationException(
            $"No plugin service registered for {typeof(TService).Name}. Ensure the required plugin is registered via Plugin<T>().");
}
```

**Step 4: Run tests**

Run: `dotnet test tests/FalkForge.Core.Tests/ --filter "FullyQualifiedName~PluginServiceRegistry"`
Expected: All 7 pass.

**Step 5: Commit**

```bash
git add src/FalkForge.Core/Plugins/PluginServiceRegistry.cs tests/FalkForge.Core.Tests/Plugins/
git commit -m "feat: implement PluginServiceRegistry with freeze and first-registration-wins"
```

---

### Task 3: Wire Plugin System into InstallerPage + InstallerUIBuilder + InstallerApp

**Files:**
- Modify: `src/FalkForge.Ui/InstallerPage.cs` — add `PluginServices` property
- Modify: `src/FalkForge.Ui/InstallerUIBuilder.cs` — add `Plugin<T>()` method
- Modify: `src/FalkForge.Ui/InstallerApp.cs` — create registry, register plugins, inject into pages, freeze

**Step 1: Add PluginServices to InstallerPage**

In `src/FalkForge.Ui/InstallerPage.cs`, add:
```csharp
public IPluginServices PluginServices { get; internal set; } = null!;
```

Add `using FalkForge.Plugins;` at top.

**Step 2: Add Plugin<T>() to InstallerUIBuilder**

Read `src/FalkForge.Ui/InstallerUIBuilder.cs` first. Add:
- A `List<IInstallerPlugin>` field
- A public property `IReadOnlyList<IInstallerPlugin> Plugins`
- A method:
```csharp
public InstallerUIBuilder Plugin<T>() where T : IInstallerPlugin, new()
{
    _plugins.Add(new T());
    return this;
}
```

**Step 3: Wire in InstallerApp.RunCore**

In `src/FalkForge.Ui/InstallerApp.cs`, in `RunCore()`, after `configure(uiBuilder)` and before page creation:
```csharp
var pluginRegistry = new PluginServiceRegistry();
foreach (var plugin in uiBuilder.Plugins)
    plugin.RegisterServices(pluginRegistry);
pluginRegistry.Freeze();
```

In the page wiring loop, add:
```csharp
page.PluginServices = pluginRegistry;
```

**Step 4: Build full solution**

Run: `dotnet build`
Expected: 0 warnings. (Existing demos don't use Plugin<T>, so no breakage.)

**Step 5: Run all tests**

Run: `dotnet test`
Expected: All pass.

**Step 6: Commit**

```bash
git add src/FalkForge.Ui/InstallerPage.cs src/FalkForge.Ui/InstallerUIBuilder.cs src/FalkForge.Ui/InstallerApp.cs
git commit -m "feat: wire plugin system into InstallerPage, UIBuilder, and InstallerApp"
```

---

### Task 4: FalkForge.Plugins.Sql — Project + Interfaces

**Files:**
- Create: `src/FalkForge.Plugins.Sql/FalkForge.Plugins.Sql.csproj`
- Create: `src/FalkForge.Plugins.Sql/ISqlServerDiscovery.cs`
- Create: `src/FalkForge.Plugins.Sql/IDatabaseLister.cs`
- Create: `src/FalkForge.Plugins.Sql/IConnectionTester.cs`
- Create: `src/FalkForge.Plugins.Sql/SqlPlugin.cs`
- Create: `tests/FalkForge.Plugins.Sql.Tests/FalkForge.Plugins.Sql.Tests.csproj`

**Step 1: Create project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <RootNamespace>FalkForge.Plugins.Sql</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../FalkForge.Core/FalkForge.Core.csproj" />
    <PackageReference Include="Microsoft.Data.SqlClient" />
  </ItemGroup>
</Project>
```

Note: Check `Directory.Packages.props` for SqlClient version. If not present, add it.

**Step 2: Create service interfaces**

`ISqlServerDiscovery.cs`:
```csharp
namespace FalkForge.Plugins.Sql;

public interface ISqlServerDiscovery
{
    Task<Result<IReadOnlyList<string>>> DiscoverServersAsync(CancellationToken ct = default);
}
```

`IDatabaseLister.cs`:
```csharp
namespace FalkForge.Plugins.Sql;

public interface IDatabaseLister
{
    Task<Result<IReadOnlyList<string>>> ListDatabasesAsync(
        string server,
        bool integratedSecurity,
        string? userName = null,
        string? password = null,
        CancellationToken ct = default);
}
```

`IConnectionTester.cs`:
```csharp
namespace FalkForge.Plugins.Sql;

public interface IConnectionTester
{
    Task<Result<Unit>> TestConnectionAsync(
        string server,
        string database,
        bool integratedSecurity,
        string? userName = null,
        string? password = null,
        CancellationToken ct = default);
}
```

**Step 3: Create SqlPlugin (stub, registers nothing yet)**

```csharp
namespace FalkForge.Plugins.Sql;

using FalkForge.Plugins;

public sealed class SqlPlugin : IInstallerPlugin
{
    public string Name => "SQL Server";

    public void RegisterServices(IPluginServiceRegistry registry)
    {
        registry.Register<ISqlServerDiscovery>(new SqlServerDiscovery());
        registry.Register<IDatabaseLister>(new DatabaseLister());
        registry.Register<IConnectionTester>(new ConnectionTester());
    }
}
```

**Step 4: Create test project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/FalkForge.Plugins.Sql/FalkForge.Plugins.Sql.csproj" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
  </ItemGroup>
</Project>
```

Note: Implementation classes (SqlServerDiscovery, DatabaseLister, ConnectionTester) will be created in Steps 5-7. For now, the SqlPlugin.RegisterServices will not compile — create minimal stubs.

**Step 5: Build**

Run: `dotnet build src/FalkForge.Plugins.Sql/`
Expected: 0 warnings (after stubs created).

**Step 6: Commit**

```bash
git add src/FalkForge.Plugins.Sql/ tests/FalkForge.Plugins.Sql.Tests/
git commit -m "feat: add FalkForge.Plugins.Sql project with service interfaces"
```

---

### Task 5: SqlServerDiscovery Implementation

**Files:**
- Create: `src/FalkForge.Plugins.Sql/SqlServerDiscovery.cs`
- Create: `tests/FalkForge.Plugins.Sql.Tests/SqlServerDiscoveryTests.cs`

**Step 1: Write tests**

Since SQL Server discovery depends on network/registry, tests should verify:
- The class implements `ISqlServerDiscovery`
- Returns `Result<IReadOnlyList<string>>` (not null)
- Does not throw on machines without SQL Server

```csharp
namespace FalkForge.Plugins.Sql.Tests;

public sealed class SqlServerDiscoveryTests
{
    [Fact]
    public async Task DiscoverServersAsync_returns_result()
    {
        var discovery = new SqlServerDiscovery();
        var result = await discovery.DiscoverServersAsync();
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
    }

    [Fact]
    public async Task DiscoverServersAsync_supports_cancellation()
    {
        var discovery = new SqlServerDiscovery();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        // Should not hang — either returns empty or throws OperationCanceledException
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => discovery.DiscoverServersAsync(cts.Token));
    }
}
```

**Step 2: Implement SqlServerDiscovery**

Uses `Microsoft.Data.SqlClient.SqlDataSourceEnumerator` for network discovery and registry probing for local instances.

```csharp
namespace FalkForge.Plugins.Sql;

using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;

internal sealed class SqlServerDiscovery : ISqlServerDiscovery
{
    public Task<Result<IReadOnlyList<string>>> DiscoverServersAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var servers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Local instances from registry
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL");
                if (key is not null)
                {
                    var machineName = Environment.MachineName;
                    foreach (var name in key.GetValueNames())
                    {
                        servers.Add(name.Equals("MSSQLSERVER", StringComparison.OrdinalIgnoreCase)
                            ? machineName
                            : $@"{machineName}\{name}");
                    }
                }
            }
            catch
            {
                // Registry access may fail — non-fatal
            }

            ct.ThrowIfCancellationRequested();

            // Network instances via SqlDataSourceEnumerator
            try
            {
                var enumerator = SqlDataSourceEnumerator.Instance;
                var table = enumerator.GetDataSources();
                foreach (DataRow row in table.Rows)
                {
                    ct.ThrowIfCancellationRequested();
                    var serverName = row["ServerName"]?.ToString();
                    var instanceName = row["InstanceName"]?.ToString();
                    if (!string.IsNullOrEmpty(serverName))
                    {
                        servers.Add(string.IsNullOrEmpty(instanceName)
                            ? serverName
                            : $@"{serverName}\{instanceName}");
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // Network enumeration may fail — non-fatal
            }

            return Result<IReadOnlyList<string>>.Success(servers.Order().ToList());
        }, ct);
    }
}
```

**Step 3: Run tests**

Run: `dotnet test tests/FalkForge.Plugins.Sql.Tests/ --filter "FullyQualifiedName~SqlServerDiscovery"`
Expected: Pass (may find 0 servers if no SQL Server installed, but Result should still be Success).

**Step 4: Commit**

```bash
git add src/FalkForge.Plugins.Sql/SqlServerDiscovery.cs tests/FalkForge.Plugins.Sql.Tests/
git commit -m "feat: implement SqlServerDiscovery with registry and network enumeration"
```

---

### Task 6: DatabaseLister + ConnectionTester Implementation

**Files:**
- Create: `src/FalkForge.Plugins.Sql/DatabaseLister.cs`
- Create: `src/FalkForge.Plugins.Sql/ConnectionTester.cs`
- Create: `src/FalkForge.Plugins.Sql/ConnectionStringBuilder.cs`
- Create: `tests/FalkForge.Plugins.Sql.Tests/ConnectionTesterTests.cs`

**Step 1: Create shared ConnectionStringBuilder helper**

```csharp
namespace FalkForge.Plugins.Sql;

using Microsoft.Data.SqlClient;

internal static class ConnectionStringHelper
{
    public static string Build(string server, string? database, bool integratedSecurity,
        string? userName, string? password, int timeoutSeconds = 5)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server,
            IntegratedSecurity = integratedSecurity,
            ConnectTimeout = timeoutSeconds,
            TrustServerCertificate = true,
            Encrypt = SqlConnectionEncryptOption.Optional,
        };
        if (!string.IsNullOrEmpty(database))
            builder.InitialCatalog = database;
        if (!integratedSecurity)
        {
            builder.UserID = userName ?? string.Empty;
            builder.Password = password ?? string.Empty;
        }
        return builder.ConnectionString;
    }
}
```

**Step 2: Implement DatabaseLister**

```csharp
namespace FalkForge.Plugins.Sql;

using Microsoft.Data.SqlClient;

internal sealed class DatabaseLister : IDatabaseLister
{
    public async Task<Result<IReadOnlyList<string>>> ListDatabasesAsync(
        string server, bool integratedSecurity,
        string? userName = null, string? password = null,
        CancellationToken ct = default)
    {
        var connStr = ConnectionStringHelper.Build(server, null, integratedSecurity, userName, password);
        try
        {
            await using var connection = new SqlConnection(connStr);
            await connection.OpenAsync(ct);

            var databases = new List<string>();
            await using var cmd = new SqlCommand("SELECT name FROM sys.databases ORDER BY name", connection);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                databases.Add(reader.GetString(0));

            return Result<IReadOnlyList<string>>.Success(databases);
        }
        catch (OperationCanceledException) { throw; }
        catch (SqlException ex)
        {
            return Result<IReadOnlyList<string>>.Failure(
                new Error(ErrorKind.PluginError, $"Failed to list databases: {ex.Message}"));
        }
    }
}
```

**Step 3: Implement ConnectionTester**

```csharp
namespace FalkForge.Plugins.Sql;

using Microsoft.Data.SqlClient;

internal sealed class ConnectionTester : IConnectionTester
{
    public async Task<Result<Unit>> TestConnectionAsync(
        string server, string database, bool integratedSecurity,
        string? userName = null, string? password = null,
        CancellationToken ct = default)
    {
        var connStr = ConnectionStringHelper.Build(server, database, integratedSecurity, userName, password);
        try
        {
            await using var connection = new SqlConnection(connStr);
            await connection.OpenAsync(ct);
            return Result<Unit>.Success(Unit.Value);
        }
        catch (OperationCanceledException) { throw; }
        catch (SqlException ex)
        {
            return Result<Unit>.Failure(
                new Error(ErrorKind.PluginError, $"Connection failed: {ex.Message}"));
        }
    }
}
```

**Step 4: Add PluginError to ErrorKind**

In `src/FalkForge.Core/ErrorKind.cs`, add `PluginError` to the enum.

**Step 5: Write tests**

```csharp
namespace FalkForge.Plugins.Sql.Tests;

public sealed class ConnectionTesterTests
{
    [Fact]
    public async Task TestConnectionAsync_invalid_server_returns_failure()
    {
        var tester = new ConnectionTester();
        var result = await tester.TestConnectionAsync(
            "NONEXISTENT_SERVER_12345", "master", true);
        Assert.True(result.IsFailure);
        Assert.Contains("Connection failed", result.Error.Message);
    }
}
```

**Step 6: Build and test**

Run: `dotnet build src/FalkForge.Plugins.Sql/ && dotnet test tests/FalkForge.Plugins.Sql.Tests/`
Expected: Build 0 warnings. Tests pass.

**Step 7: Commit**

```bash
git add src/FalkForge.Core/ErrorKind.cs src/FalkForge.Plugins.Sql/ tests/FalkForge.Plugins.Sql.Tests/
git commit -m "feat: implement DatabaseLister, ConnectionTester, and ConnectionStringHelper"
```

---

### Task 7: FalkForge.Plugins.Odbc

**Files:**
- Create: `src/FalkForge.Plugins.Odbc/FalkForge.Plugins.Odbc.csproj`
- Create: `src/FalkForge.Plugins.Odbc/IOdbcManager.cs`
- Create: `src/FalkForge.Plugins.Odbc/OdbcManager.cs`
- Create: `src/FalkForge.Plugins.Odbc/OdbcPlugin.cs`
- Create: `tests/FalkForge.Plugins.Odbc.Tests/FalkForge.Plugins.Odbc.Tests.csproj`
- Create: `tests/FalkForge.Plugins.Odbc.Tests/OdbcManagerTests.cs`

**Step 1: Create project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <RootNamespace>FalkForge.Plugins.Odbc</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../FalkForge.Core/FalkForge.Core.csproj" />
  </ItemGroup>
</Project>
```

**Step 2: Create IOdbcManager**

```csharp
namespace FalkForge.Plugins.Odbc;

public interface IOdbcManager
{
    Result<bool> DsnExists(string dsnName);
    void LaunchOdbcAdministrator();
}
```

**Step 3: Implement OdbcManager**

```csharp
namespace FalkForge.Plugins.Odbc;

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

internal sealed class OdbcManager : IOdbcManager
{
    public Result<bool> DsnExists(string dsnName)
    {
        if (string.IsNullOrWhiteSpace(dsnName))
            return Result<bool>.Failure(new Error(ErrorKind.Validation, "DSN name cannot be empty."));

        try
        {
            // Check both 32-bit and 64-bit ODBC DSN locations
            var exists = CheckRegistry(@"SOFTWARE\ODBC\ODBC.INI", dsnName)
                      || CheckRegistry(@"SOFTWARE\WOW6432Node\ODBC\ODBC.INI", dsnName);
            return Result<bool>.Success(exists);
        }
        catch (Exception ex)
        {
            return Result<bool>.Failure(new Error(ErrorKind.PluginError, $"Failed to check DSN: {ex.Message}"));
        }
    }

    public void LaunchOdbcAdministrator()
    {
        var path = Path.Combine(Environment.SystemDirectory, "odbcad32.exe");
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static bool CheckRegistry(string basePath, string dsnName)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"{basePath}\{dsnName}");
        if (key is not null) return true;
        using var userKey = Registry.CurrentUser.OpenSubKey($@"{basePath}\{dsnName}");
        return userKey is not null;
    }
}
```

**Step 4: Create OdbcPlugin**

```csharp
namespace FalkForge.Plugins.Odbc;

using FalkForge.Plugins;

public sealed class OdbcPlugin : IInstallerPlugin
{
    public string Name => "ODBC";

    public void RegisterServices(IPluginServiceRegistry registry)
    {
        registry.Register<IOdbcManager>(new OdbcManager());
    }
}
```

**Step 5: Write tests**

```csharp
namespace FalkForge.Plugins.Odbc.Tests;

public sealed class OdbcManagerTests
{
    [Fact]
    public void DsnExists_empty_name_returns_failure()
    {
        var manager = new OdbcManager();
        var result = manager.DsnExists("");
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void DsnExists_nonexistent_dsn_returns_false()
    {
        var manager = new OdbcManager();
        var result = manager.DsnExists("NONEXISTENT_DSN_TEST_12345");
        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void Plugin_registers_IOdbcManager()
    {
        var registry = new PluginServiceRegistry();
        var plugin = new OdbcPlugin();
        plugin.RegisterServices(registry);

        IPluginServices services = registry;
        Assert.NotNull(services.GetService<IOdbcManager>());
    }
}
```

**Step 6: Build and test**

Run: `dotnet build src/FalkForge.Plugins.Odbc/ && dotnet test tests/FalkForge.Plugins.Odbc.Tests/`

**Step 7: Commit**

```bash
git add src/FalkForge.Plugins.Odbc/ tests/FalkForge.Plugins.Odbc.Tests/
git commit -m "feat: add FalkForge.Plugins.Odbc with DSN checking and admin launcher"
```

---

### Task 8: FalkForge.Plugins.FileSystem

**Files:**
- Create: `src/FalkForge.Plugins.FileSystem/FalkForge.Plugins.FileSystem.csproj`
- Create: `src/FalkForge.Plugins.FileSystem/IFolderBrowser.cs`
- Create: `src/FalkForge.Plugins.FileSystem/FolderBrowser.cs`
- Create: `src/FalkForge.Plugins.FileSystem/FileSystemPlugin.cs`

**Step 1: Create project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <RootNamespace>FalkForge.Plugins.FileSystem</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../FalkForge.Core/FalkForge.Core.csproj" />
  </ItemGroup>
</Project>
```

**Step 2: Create IFolderBrowser**

```csharp
namespace FalkForge.Plugins.FileSystem;

public interface IFolderBrowser
{
    string? BrowseForFolder(string? initialDirectory = null, string? description = null);
}
```

**Step 3: Implement FolderBrowser using WPF OpenFolderDialog (.NET 8+)**

```csharp
namespace FalkForge.Plugins.FileSystem;

using Microsoft.Win32;

internal sealed class FolderBrowser : IFolderBrowser
{
    public string? BrowseForFolder(string? initialDirectory = null, string? description = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = description ?? "Select folder",
            Multiselect = false,
        };

        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
            dialog.InitialDirectory = initialDirectory;

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
```

**Step 4: Create FileSystemPlugin**

```csharp
namespace FalkForge.Plugins.FileSystem;

using FalkForge.Plugins;

public sealed class FileSystemPlugin : IInstallerPlugin
{
    public string Name => "FileSystem";

    public void RegisterServices(IPluginServiceRegistry registry)
    {
        registry.Register<IFolderBrowser>(new FolderBrowser());
    }
}
```

**Step 5: Build**

Run: `dotnet build src/FalkForge.Plugins.FileSystem/`

**Step 6: Commit**

```bash
git add src/FalkForge.Plugins.FileSystem/
git commit -m "feat: add FalkForge.Plugins.FileSystem with folder browser dialog"
```

---

### Task 9: Wire MAS Demo to Use Plugins

**Files:**
- Modify: `demo/MAS/MAS.csproj` — add plugin project references
- Modify: `demo/MAS/Program.cs` — register plugins
- Modify: `demo/MAS/Pages/DatabaseServerPage.cs` — use ISqlServerDiscovery
- Modify: `demo/MAS/Views/DatabaseServerView.xaml` — bind search button
- Modify: `demo/MAS/Pages/DatabaseConnectionSettingsPage.cs` — use IConnectionTester
- Modify: `demo/MAS/Views/DatabaseConnectionSettingsView.xaml` — bind test button
- Modify: `demo/MAS/Pages/MultiServerAdvancedSettingsPage.cs` — use IOdbcManager
- Modify: `demo/MAS/Pages/MultiServerExAdvancedSettingsPage.cs` — use IOdbcManager
- Modify: `demo/MAS/Pages/AdvancedInstallDirMultiServerPage.cs` — use IFolderBrowser
- Modify: `demo/MAS/Pages/AdvancedInstallDirMultiServerExPage.cs` — use IFolderBrowser

**Step 1: Add project references to MAS.csproj**

```xml
<ProjectReference Include="../../src/FalkForge.Plugins.Sql/FalkForge.Plugins.Sql.csproj" />
<ProjectReference Include="../../src/FalkForge.Plugins.Odbc/FalkForge.Plugins.Odbc.csproj" />
<ProjectReference Include="../../src/FalkForge.Plugins.FileSystem/FalkForge.Plugins.FileSystem.csproj" />
```

**Step 2: Register plugins in Program.cs**

```csharp
using FalkForge.Plugins.Sql;
using FalkForge.Plugins.Odbc;
using FalkForge.Plugins.FileSystem;

return InstallerApp.Run(args, app => app
    .Plugin<SqlPlugin>()
    .Plugin<OdbcPlugin>()
    .Plugin<FileSystemPlugin>()
    .Window(w => w.CustomWindow<MasInstallerWindow>())
    .Pages(p => p...));
```

**Step 3: Wire DatabaseServerPage — Search for server**

Add an async command pattern to DatabaseServerPage:
```csharp
private bool _isSearching;
public bool IsSearching { get => _isSearching; set => SetField(ref _isSearching, value); }
public ObservableCollection<string> AvailableServers { get; } = [];

public async Task SearchServersAsync()
{
    var discovery = PluginServices.GetService<ISqlServerDiscovery>();
    if (discovery is null) return;
    IsSearching = true;
    try
    {
        var result = await discovery.DiscoverServersAsync();
        if (result.IsSuccess)
        {
            AvailableServers.Clear();
            foreach (var server in result.Value)
                AvailableServers.Add(server);
        }
    }
    finally { IsSearching = false; }
}
```

Update DatabaseServerView.xaml — the Search button gets a Click handler in code-behind.

**Step 4: Wire DatabaseConnectionSettingsPage — Test connection**

Add test connection command:
```csharp
private string _testResult = string.Empty;
public string TestResult { get => _testResult; set => SetField(ref _testResult, value); }

public async Task TestConnectionAsync()
{
    var tester = PluginServices.GetService<IConnectionTester>();
    if (tester is null) return;
    TestResult = "Testing...";
    var result = await tester.TestConnectionAsync(
        _databaseServer, _databaseName, _integratedSecurity, _userName, _password);
    TestResult = result.IsSuccess ? "Connection successful!" : $"Failed: {result.Error.Message}";
}
```

**Step 5: Wire MultiServer/Ex pages — Check DSN + Launch ODBC admin**

```csharp
public void CheckDsnName()
{
    var odbc = PluginServices.GetService<IOdbcManager>();
    if (odbc is null) return;
    var result = odbc.DsnExists(_dsnName);
    if (result.IsSuccess && result.Value)
        DsnWarning = $"DSN name, {_dsnName}, already exists. Observe if using this name the installation will not overwrite existing settings.";
    else
        DsnWarning = string.Empty;
}

public void LaunchOdbcAdmin()
{
    var odbc = PluginServices.GetService<IOdbcManager>();
    odbc?.LaunchOdbcAdministrator();
}
```

**Step 6: Wire Install Dir pages — Browse folder**

```csharp
public void BrowseFolder()
{
    var browser = PluginServices.GetService<IFolderBrowser>();
    if (browser is null) return;
    var folder = browser.BrowseForFolder(_installFolder, "Select installation folder");
    if (folder is not null) InstallFolder = folder;
}
```

**Step 7: Update view XAML files with Click handlers**

Each button needs a Click handler in the code-behind that calls the page's async method. The code-behind pattern:
```csharp
private async void SearchServer_Click(object sender, RoutedEventArgs e)
{
    if (DataContext is DatabaseServerPage page)
        await page.SearchServersAsync();
}
```

**Step 8: Build and run**

Run: `dotnet build demo/MAS/MAS.csproj && dotnet run --project demo/MAS/MAS.csproj`

Test: Search for server, test connection, check DSN, browse folders.

**Step 9: Commit**

```bash
git add demo/MAS/
git commit -m "feat: wire MAS demo to use SQL, ODBC, and FileSystem plugins"
```

---

### Task 10: Update CLAUDE.md + Final Verification

**Files:**
- Modify: `CLAUDE.md` — add plugin projects to solution structure, dependency graph, namespace conventions

**Step 1: Update CLAUDE.md**

Add to Solution Structure:
```
  FalkForge.Plugins.Sql/         # SQL Server discovery, listing, connection testing
  FalkForge.Plugins.Odbc/        # ODBC DSN checking, admin launcher
  FalkForge.Plugins.FileSystem/  # Folder browser dialog
```

Add to Dependency Graph:
```
  +-> Plugins.Sql (Core + Microsoft.Data.SqlClient)
  +-> Plugins.Odbc (Core, P/Invoke, Windows-only)
  +-> Plugins.FileSystem (Core, WPF, Windows-only)
```

Add namespace conventions:
```
FalkForge.Plugins                   Plugin infrastructure (Core)
FalkForge.Plugins.Sql               SQL Server plugin
FalkForge.Plugins.Odbc              ODBC plugin
FalkForge.Plugins.FileSystem        FileSystem plugin
```

**Step 2: Full build**

Run: `dotnet build`
Expected: 0 warnings.

**Step 3: Full test suite**

Run: `dotnet test`
Expected: All tests pass.

**Step 4: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: update CLAUDE.md with plugin system projects and namespaces"
```
