# HTTP Extension Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task.

**Goal:** Implement `Extensions.Http` — a new FalkForge extension that generates MSI custom actions to configure Windows URL ACL reservations and SNI SSL certificate bindings via `netsh.exe`.

**Architecture:** Two `IMsiTableContributor` classes generate rows for the `CustomAction` and `InstallExecuteSequence` MSI tables. Deferred elevated CAs (type 3106) run `netsh.exe` from `[SystemFolder]`. A standalone validator (`HttpValidator`) covers nine error codes HTTP001–HTTP009. `HttpExtension` wires everything together as `IFalkForgeExtension`.

**Tech Stack:** C# 13, .NET 10, xUnit 2.9.3, `FalkForge.Core` (Error/ErrorKind/Result), `FalkForge.Extensibility` (IFalkForgeExtension/IMsiTableContributor/IExtensionRegistry/MsiTableRow/ExtensionContext)

---

## Context

**Design doc:** `docs/plans/2026-02-25-http-extension-design.md` — read this before starting.

**Worktree branch:** `feature/http-extension` at `.worktrees/feature-http-extension/`

**Key patterns to reference before implementing:**
- `src/FalkForge.Extensions.Dependency/DependencyTableContributor.cs` — `IMsiTableContributor` pattern
- `src/FalkForge.Extensions.Firewall/FirewallExtension.cs` — extension entry point pattern
- `tests/FalkForge.Extensions.Dependency.Tests/DependencyTableContributorTests.cs` — contributor test pattern
- `src/FalkForge.Extensibility/IExtensionRegistry.cs` — `RegisterTableContributor` method

**MSI Custom Action type numbers used throughout this plan:**
- `3106` = deferred + no-impersonate (elevated): `34 + 0x400 (1024) + 0x800 (2048)` — runs netsh, installs or removes
- `3362` = rollback + no-impersonate: `34 + 0x400 (1024) + 0x100 (256) + 0x800 (2048)` — undo if install fails

**Sequence numbers:**
- Add actions: `4150 + n` (after InstallFiles ~4100)
- Rollback actions: `4050 + n` (before Add, so rollback CA is scheduled before its deferred twin)
- Remove actions: `3650 + n` (before RemoveFiles ~3700)

---

### Task 1: Project Setup

**Files:**
- Create: `src/FalkForge.Extensions.Http/FalkForge.Extensions.Http.csproj`
- Create: `tests/FalkForge.Extensions.Http.Tests/FalkForge.Extensions.Http.Tests.csproj`
- Modify: `FalkForge.slnx`

**Step 1: Create source project file**

```xml
<!-- src/FalkForge.Extensions.Http/FalkForge.Extensions.Http.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>FalkForge.Extensions.Http</RootNamespace>
    <Description>HTTP extension for FalkForge — manages URL ACL reservations and SNI SSL bindings during installation via netsh custom actions</Description>
  </PropertyGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="FalkForge.Extensions.Http.Tests" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\FalkForge.Core\FalkForge.Core.csproj" />
    <ProjectReference Include="..\FalkForge.Extensibility\FalkForge.Extensibility.csproj" />
  </ItemGroup>
</Project>
```

**Step 2: Create test project file**

```xml
<!-- tests/FalkForge.Extensions.Http.Tests/FalkForge.Extensions.Http.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="coverlet.collector" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\FalkForge.Extensions.Http\FalkForge.Extensions.Http.csproj" />
    <ProjectReference Include="..\..\src\FalkForge.Testing\FalkForge.Testing.csproj" />
  </ItemGroup>
</Project>
```

**Step 3: Register in FalkForge.slnx**

Add these two lines to `FalkForge.slnx` alongside the other extension entries (alphabetical order):

```xml
<Project Path="src/FalkForge.Extensions.Http/FalkForge.Extensions.Http.csproj" />
```

```xml
<Project Path="tests/FalkForge.Extensions.Http.Tests/FalkForge.Extensions.Http.Tests.csproj" />
```

**Step 4: Verify build**

Run: `dotnet build src/FalkForge.Extensions.Http/FalkForge.Extensions.Http.csproj`
Expected: Build succeeded, 0 warnings

Run: `dotnet build tests/FalkForge.Extensions.Http.Tests/FalkForge.Extensions.Http.Tests.csproj`
Expected: Build succeeded, 0 warnings

**Step 5: Commit**

```bash
git add src/FalkForge.Extensions.Http/ tests/FalkForge.Extensions.Http.Tests/ FalkForge.slnx
git commit -m "feat: add Extensions.Http project scaffolding"
```

---

### Task 2: Models

**Files:**
- Create: `src/FalkForge.Extensions.Http/Models/UrlReservationModel.cs`
- Create: `src/FalkForge.Extensions.Http/Models/SniSslBindingModel.cs`
- Create: `tests/FalkForge.Extensions.Http.Tests/Models/UrlReservationModelTests.cs`
- Create: `tests/FalkForge.Extensions.Http.Tests/Models/SniSslBindingModelTests.cs`

**Step 1: Write failing tests**

```csharp
// tests/FalkForge.Extensions.Http.Tests/Models/UrlReservationModelTests.cs
using FalkForge.Extensions.Http.Models;

namespace FalkForge.Extensions.Http.Tests.Models;

public sealed class UrlReservationModelTests
{
    [Fact]
    public void UrlReservationModel_StoresUrlAndUser()
    {
        var model = new UrlReservationModel("http://+:8080/svc/", "D:(A;;GX;;;NS)");

        Assert.Equal("http://+:8080/svc/", model.Url);
        Assert.Equal("D:(A;;GX;;;NS)", model.User);
    }
}
```

```csharp
// tests/FalkForge.Extensions.Http.Tests/Models/SniSslBindingModelTests.cs
using FalkForge.Extensions.Http.Models;

namespace FalkForge.Extensions.Http.Tests.Models;

public sealed class SniSslBindingModelTests
{
    private const string ValidThumbprint = "ABCDEF1234567890ABCDEF1234567890ABCDEF12";

    [Fact]
    public void SniSslBindingModel_StoresAllProperties()
    {
        var appId = Guid.NewGuid();
        var model = new SniSslBindingModel("api.example.com", 443, ValidThumbprint, appId);

        Assert.Equal("api.example.com", model.Hostname);
        Assert.Equal(443, model.Port);
        Assert.Equal(ValidThumbprint, model.CertificateThumbprint);
        Assert.Equal(appId, model.AppId);
        Assert.Equal("MY", model.CertStoreName);
    }

    [Fact]
    public void SniSslBindingModel_CustomCertStoreName_IsStored()
    {
        var model = new SniSslBindingModel("host", 443, ValidThumbprint, Guid.NewGuid(), "TrustedPeople");

        Assert.Equal("TrustedPeople", model.CertStoreName);
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/FalkForge.Extensions.Http.Tests/ -q`
Expected: FAIL — type not found

**Step 3: Implement models**

```csharp
// src/FalkForge.Extensions.Http/Models/UrlReservationModel.cs
namespace FalkForge.Extensions.Http.Models;

public sealed record UrlReservationModel(string Url, string User);
```

```csharp
// src/FalkForge.Extensions.Http/Models/SniSslBindingModel.cs
namespace FalkForge.Extensions.Http.Models;

public sealed record SniSslBindingModel(
    string Hostname,
    int Port,
    string CertificateThumbprint,
    Guid AppId,
    string CertStoreName = "MY");
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/FalkForge.Extensions.Http.Tests/ -q`
Expected: PASS — 3 tests

**Step 5: Commit**

```bash
git add src/FalkForge.Extensions.Http/Models/ tests/FalkForge.Extensions.Http.Tests/Models/
git commit -m "feat: add UrlReservationModel and SniSslBindingModel"
```

---

### Task 3: Builders

**Files:**
- Create: `src/FalkForge.Extensions.Http/Builders/UrlReservationBuilder.cs`
- Create: `src/FalkForge.Extensions.Http/Builders/SniSslBindingBuilder.cs`
- Create: `tests/FalkForge.Extensions.Http.Tests/Builders/UrlReservationBuilderTests.cs`
- Create: `tests/FalkForge.Extensions.Http.Tests/Builders/SniSslBindingBuilderTests.cs`

**Step 1: Write failing tests**

```csharp
// tests/FalkForge.Extensions.Http.Tests/Builders/UrlReservationBuilderTests.cs
using FalkForge.Extensions.Http.Builders;

namespace FalkForge.Extensions.Http.Tests.Builders;

public sealed class UrlReservationBuilderTests
{
    [Fact]
    public void AllowNetworkService_SetsExpectedSddl()
    {
        var model = new UrlReservationBuilder("http://+:8080/svc/").AllowNetworkService().Build();
        Assert.Equal("D:(A;;GX;;;NS)", model.User);
    }

    [Fact]
    public void AllowLocalService_SetsExpectedSddl()
    {
        var model = new UrlReservationBuilder("http://+:8080/svc/").AllowLocalService().Build();
        Assert.Equal("D:(A;;GX;;;LS)", model.User);
    }

    [Fact]
    public void AllowLocalSystem_SetsExpectedSddl()
    {
        var model = new UrlReservationBuilder("http://+:8080/svc/").AllowLocalSystem().Build();
        Assert.Equal("D:(A;;GX;;;SY)", model.User);
    }

    [Fact]
    public void AllowEveryone_SetsExpectedSddl()
    {
        var model = new UrlReservationBuilder("http://+:8080/svc/").AllowEveryone().Build();
        Assert.Equal("D:(A;;GX;;;WD)", model.User);
    }

    [Fact]
    public void AllowBuiltinUsers_SetsExpectedSddl()
    {
        var model = new UrlReservationBuilder("http://+:8080/svc/").AllowBuiltinUsers().Build();
        Assert.Equal("D:(A;;GX;;;BU)", model.User);
    }

    [Fact]
    public void AllowUser_SetsArbitraryValue()
    {
        var model = new UrlReservationBuilder("http://+:8080/svc/").AllowUser("DOMAIN\\SvcAccount").Build();
        Assert.Equal("DOMAIN\\SvcAccount", model.User);
    }

    [Fact]
    public void Build_PreservesUrl()
    {
        var model = new UrlReservationBuilder("http://+:9090/api/").AllowEveryone().Build();
        Assert.Equal("http://+:9090/api/", model.Url);
    }
}
```

```csharp
// tests/FalkForge.Extensions.Http.Tests/Builders/SniSslBindingBuilderTests.cs
using FalkForge.Extensions.Http.Builders;

namespace FalkForge.Extensions.Http.Tests.Builders;

public sealed class SniSslBindingBuilderTests
{
    private const string ValidThumbprint = "ABCDEF1234567890ABCDEF1234567890ABCDEF12";

    [Fact]
    public void AppId_AutoDerived_IsNotEmpty()
    {
        var model = new SniSslBindingBuilder("api.example.com", 443)
            .Thumbprint(ValidThumbprint)
            .Build();

        Assert.NotEqual(Guid.Empty, model.AppId);
    }

    [Fact]
    public void AppId_AutoDerived_IsDeterministic()
    {
        var model1 = new SniSslBindingBuilder("api.example.com", 443).Thumbprint(ValidThumbprint).Build();
        var model2 = new SniSslBindingBuilder("api.example.com", 443).Thumbprint(ValidThumbprint).Build();

        Assert.Equal(model1.AppId, model2.AppId);
    }

    [Fact]
    public void AppId_AutoDerived_DiffersForDifferentHostPort()
    {
        var model1 = new SniSslBindingBuilder("api.example.com", 443).Thumbprint(ValidThumbprint).Build();
        var model2 = new SniSslBindingBuilder("api.example.com", 8443).Thumbprint(ValidThumbprint).Build();

        Assert.NotEqual(model1.AppId, model2.AppId);
    }

    [Fact]
    public void AppId_ExplicitOverride_IsUsed()
    {
        var explicit = Guid.NewGuid();
        var model = new SniSslBindingBuilder("api.example.com", 443)
            .Thumbprint(ValidThumbprint)
            .AppId(explicit)
            .Build();

        Assert.Equal(explicit, model.AppId);
    }

    [Fact]
    public void CertStoreName_DefaultIsMY()
    {
        var model = new SniSslBindingBuilder("host", 443).Thumbprint(ValidThumbprint).Build();

        Assert.Equal("MY", model.CertStoreName);
    }

    [Fact]
    public void CertStoreName_Custom_IsStored()
    {
        var model = new SniSslBindingBuilder("host", 443)
            .Thumbprint(ValidThumbprint)
            .CertStoreName("TrustedPeople")
            .Build();

        Assert.Equal("TrustedPeople", model.CertStoreName);
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/FalkForge.Extensions.Http.Tests/ -q`
Expected: FAIL — type not found

**Step 3: Implement builders**

```csharp
// src/FalkForge.Extensions.Http/Builders/UrlReservationBuilder.cs
using FalkForge.Extensions.Http.Models;

namespace FalkForge.Extensions.Http.Builders;

public sealed class UrlReservationBuilder(string url)
{
    private string _user = "";

    public UrlReservationBuilder AllowNetworkService() => AllowUser("D:(A;;GX;;;NS)");
    public UrlReservationBuilder AllowLocalService()   => AllowUser("D:(A;;GX;;;LS)");
    public UrlReservationBuilder AllowLocalSystem()    => AllowUser("D:(A;;GX;;;SY)");
    public UrlReservationBuilder AllowEveryone()       => AllowUser("D:(A;;GX;;;WD)");
    public UrlReservationBuilder AllowBuiltinUsers()   => AllowUser("D:(A;;GX;;;BU)");

    public UrlReservationBuilder AllowUser(string sddlOrUser)
    {
        _user = sddlOrUser;
        return this;
    }

    internal UrlReservationModel Build() => new(url, _user);
}
```

```csharp
// src/FalkForge.Extensions.Http/Builders/SniSslBindingBuilder.cs
using System.Security.Cryptography;
using System.Text;
using FalkForge.Extensions.Http.Models;

namespace FalkForge.Extensions.Http.Builders;

public sealed class SniSslBindingBuilder(string hostname, int port)
{
    private string _thumbprint = "";
    private Guid   _appId;
    private string _certStoreName = "MY";

    public SniSslBindingBuilder Thumbprint(string thumbprint)   { _thumbprint = thumbprint; return this; }
    public SniSslBindingBuilder AppId(Guid appId)               { _appId = appId;           return this; }
    public SniSslBindingBuilder CertStoreName(string storeName) { _certStoreName = storeName; return this; }

    internal SniSslBindingModel Build()
    {
        var appId = _appId == Guid.Empty ? DeriveAppId(hostname, port) : _appId;
        return new SniSslBindingModel(hostname, port, _thumbprint, appId, _certStoreName);
    }

    private static Guid DeriveAppId(string host, int p)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{host}:{p}"));
        return new Guid(bytes[..16]);
    }
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/FalkForge.Extensions.Http.Tests/ -q`
Expected: PASS — 13 tests

**Step 5: Commit**

```bash
git add src/FalkForge.Extensions.Http/Builders/ tests/FalkForge.Extensions.Http.Tests/Builders/
git commit -m "feat: add UrlReservationBuilder and SniSslBindingBuilder"
```

---

### Task 4: HttpValidator

**Files:**
- Create: `src/FalkForge.Extensions.Http/Validation/HttpValidator.cs`
- Create: `tests/FalkForge.Extensions.Http.Tests/Validation/HttpValidatorTests.cs`

**Step 1: Write failing tests**

```csharp
// tests/FalkForge.Extensions.Http.Tests/Validation/HttpValidatorTests.cs
using FalkForge.Extensions.Http.Models;
using FalkForge.Extensions.Http.Validation;

namespace FalkForge.Extensions.Http.Tests.Validation;

public sealed class HttpValidatorTests
{
    private const string ValidThumbprint = "ABCDEF1234567890ABCDEF1234567890ABCDEF12";
    private const string ValidSddl = "D:(A;;GX;;;NS)";

    // --- URL Reservation ---

    [Fact]
    public void HTTP001_EmptyUrl_ReturnsError()
    {
        var errors = HttpValidator.ValidateReservations([new UrlReservationModel("", ValidSddl)]);
        Assert.Contains(errors, e => e.Message.StartsWith("HTTP001"));
    }

    [Fact]
    public void HTTP002_UrlWithoutHttpPrefix_ReturnsError()
    {
        var errors = HttpValidator.ValidateReservations([new UrlReservationModel("ftp://+:21/", ValidSddl)]);
        Assert.Contains(errors, e => e.Message.StartsWith("HTTP002"));
    }

    [Fact]
    public void HTTP003_UrlWithoutTrailingSlash_ReturnsError()
    {
        var errors = HttpValidator.ValidateReservations([new UrlReservationModel("http://+:8080/svc", ValidSddl)]);
        Assert.Contains(errors, e => e.Message.StartsWith("HTTP003"));
    }

    [Fact]
    public void HTTP004_EmptyUser_ReturnsError()
    {
        var errors = HttpValidator.ValidateReservations([new UrlReservationModel("http://+:8080/svc/", "")]);
        Assert.Contains(errors, e => e.Message.StartsWith("HTTP004"));
    }

    [Fact]
    public void ValidReservation_ReturnsNoErrors()
    {
        var errors = HttpValidator.ValidateReservations([new UrlReservationModel("https://+:443/api/", ValidSddl)]);
        Assert.Empty(errors);
    }

    // --- SNI SSL Binding ---

    [Fact]
    public void HTTP005_EmptyHostname_ReturnsError()
    {
        var errors = HttpValidator.ValidateBindings([new SniSslBindingModel("", 443, ValidThumbprint, Guid.NewGuid())]);
        Assert.Contains(errors, e => e.Message.StartsWith("HTTP005"));
    }

    [Fact]
    public void HTTP006_PortZero_ReturnsError()
    {
        var errors = HttpValidator.ValidateBindings([new SniSslBindingModel("host", 0, ValidThumbprint, Guid.NewGuid())]);
        Assert.Contains(errors, e => e.Message.StartsWith("HTTP006"));
    }

    [Fact]
    public void HTTP006_Port65536_ReturnsError()
    {
        var errors = HttpValidator.ValidateBindings([new SniSslBindingModel("host", 65536, ValidThumbprint, Guid.NewGuid())]);
        Assert.Contains(errors, e => e.Message.StartsWith("HTTP006"));
    }

    [Fact]
    public void HTTP007_EmptyThumbprint_ReturnsError()
    {
        var errors = HttpValidator.ValidateBindings([new SniSslBindingModel("host", 443, "", Guid.NewGuid())]);
        Assert.Contains(errors, e => e.Message.StartsWith("HTTP007"));
    }

    [Fact]
    public void HTTP008_ThumbprintNot40Chars_ReturnsError()
    {
        var errors = HttpValidator.ValidateBindings([new SniSslBindingModel("host", 443, "ABCDEF12", Guid.NewGuid())]);
        Assert.Contains(errors, e => e.Message.StartsWith("HTTP008"));
    }

    [Fact]
    public void HTTP008_ThumbprintWithNonHex_ReturnsError()
    {
        var errors = HttpValidator.ValidateBindings([new SniSslBindingModel("host", 443, "ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ", Guid.NewGuid())]);
        Assert.Contains(errors, e => e.Message.StartsWith("HTTP008"));
    }

    [Fact]
    public void HTTP009_EmptyGuidAppId_ReturnsError()
    {
        var errors = HttpValidator.ValidateBindings([new SniSslBindingModel("host", 443, ValidThumbprint, Guid.Empty)]);
        Assert.Contains(errors, e => e.Message.StartsWith("HTTP009"));
    }

    [Fact]
    public void ValidBinding_ReturnsNoErrors()
    {
        var errors = HttpValidator.ValidateBindings([new SniSslBindingModel("api.example.com", 443, ValidThumbprint, Guid.NewGuid())]);
        Assert.Empty(errors);
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/FalkForge.Extensions.Http.Tests/ -q`
Expected: FAIL — type not found

**Step 3: Implement validator**

```csharp
// src/FalkForge.Extensions.Http/Validation/HttpValidator.cs
using FalkForge;
using FalkForge.Extensions.Http.Models;

namespace FalkForge.Extensions.Http.Validation;

internal static class HttpValidator
{
    internal static IReadOnlyList<Error> ValidateReservations(IReadOnlyList<UrlReservationModel> reservations)
    {
        var errors = new List<Error>();
        foreach (var r in reservations)
        {
            if (string.IsNullOrWhiteSpace(r.Url))
            {
                errors.Add(new Error(ErrorKind.Validation, "HTTP001: URL reservation URL must not be empty."));
                continue;
            }
            if (!r.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !r.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                errors.Add(new Error(ErrorKind.Validation, $"HTTP002: URL reservation URL '{r.Url}' must start with http:// or https://."));

            if (!r.Url.EndsWith('/'))
                errors.Add(new Error(ErrorKind.Validation, $"HTTP003: URL reservation URL '{r.Url}' must end with '/'."));

            if (string.IsNullOrWhiteSpace(r.User))
                errors.Add(new Error(ErrorKind.Validation, "HTTP004: URL reservation User/SDDL string must not be empty."));
        }
        return errors;
    }

    internal static IReadOnlyList<Error> ValidateBindings(IReadOnlyList<SniSslBindingModel> bindings)
    {
        var errors = new List<Error>();
        foreach (var b in bindings)
        {
            if (string.IsNullOrWhiteSpace(b.Hostname))
                errors.Add(new Error(ErrorKind.Validation, "HTTP005: SNI SSL binding Hostname must not be empty."));

            if (b.Port is < 1 or > 65535)
                errors.Add(new Error(ErrorKind.Validation, $"HTTP006: SNI SSL binding Port {b.Port} is outside valid range 1–65535."));

            if (string.IsNullOrWhiteSpace(b.CertificateThumbprint))
                errors.Add(new Error(ErrorKind.Validation, "HTTP007: SNI SSL binding CertificateThumbprint must not be empty."));
            else if (b.CertificateThumbprint.Length != 40 || !IsAllHex(b.CertificateThumbprint))
                errors.Add(new Error(ErrorKind.Validation, $"HTTP008: SNI SSL binding CertificateThumbprint '{b.CertificateThumbprint}' must be exactly 40 hexadecimal characters."));

            if (b.AppId == Guid.Empty)
                errors.Add(new Error(ErrorKind.Validation, "HTTP009: SNI SSL binding AppId must not be an empty GUID."));
        }
        return errors;
    }

    private static bool IsAllHex(string s)
    {
        foreach (var c in s)
            if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F')))
                return false;
        return true;
    }
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/FalkForge.Extensions.Http.Tests/ -q`
Expected: PASS — 27 tests

**Step 5: Commit**

```bash
git add src/FalkForge.Extensions.Http/Validation/ tests/FalkForge.Extensions.Http.Tests/Validation/
git commit -m "feat: add HttpValidator with HTTP001-HTTP009 error codes"
```

---

### Task 5: HttpCustomActionContributor

**Files:**
- Create: `src/FalkForge.Extensions.Http/Compilation/HttpCustomActionContributor.cs`
- Create: `tests/FalkForge.Extensions.Http.Tests/Compilation/HttpCustomActionContributorTests.cs`

**Step 1: Write failing tests**

```csharp
// tests/FalkForge.Extensions.Http.Tests/Compilation/HttpCustomActionContributorTests.cs
using FalkForge.Extensibility;
using FalkForge.Extensions.Http.Compilation;
using FalkForge.Extensions.Http.Models;
using FalkForge.Models;

namespace FalkForge.Extensions.Http.Tests.Compilation;

public sealed class HttpCustomActionContributorTests
{
    private static readonly ExtensionContext EmptyContext = new()
    {
        Package = new PackageModel
        {
            Name = "Test", Manufacturer = "Test", Version = new Version(1, 0, 0)
        },
        OutputDirectory = "out",
        SourceDirectory = "src"
    };

    private const string ValidThumbprint = "ABCDEF1234567890ABCDEF1234567890ABCDEF12";

    [Fact]
    public void TableName_IsCustomAction()
    {
        var contributor = new HttpCustomActionContributor([], []);
        Assert.Equal("CustomAction", contributor.TableName);
    }

    [Fact]
    public void NoReservationsOrBindings_ReturnsEmpty()
    {
        var contributor = new HttpCustomActionContributor([], []);
        Assert.Empty(contributor.GetRows(EmptyContext));
    }

    [Fact]
    public void OneUrlReservation_EmitsThreeRows()
    {
        var reservations = new List<UrlReservationModel>
        {
            new("http://+:8080/svc/", "D:(A;;GX;;;NS)")
        };
        var contributor = new HttpCustomActionContributor(reservations, []);

        Assert.Equal(3, contributor.GetRows(EmptyContext).Count);
    }

    [Fact]
    public void UrlReservation_AddRow_HasCorrectCommand()
    {
        var reservations = new List<UrlReservationModel> { new("http://+:8080/svc/", "D:(A;;GX;;;NS)") };
        var contributor = new HttpCustomActionContributor(reservations, []);
        var rows = contributor.GetRows(EmptyContext);

        var addRow = rows.Single(r => r.Get("Action") is string a && a.StartsWith("HttpAddUrlAcl_"));
        Assert.Equal("SystemFolder", addRow.Get("Source"));
        var target = (string)addRow.Get("Target")!;
        Assert.Contains("netsh.exe http add urlacl", target);
        Assert.Contains("http://+:8080/svc/", target);
        Assert.Contains("D:(A;;GX;;;NS)", target);
    }

    [Fact]
    public void UrlReservation_AddRow_IsType3106()
    {
        var reservations = new List<UrlReservationModel> { new("http://+:8080/svc/", "D:(A;;GX;;;NS)") };
        var contributor = new HttpCustomActionContributor(reservations, []);
        var rows = contributor.GetRows(EmptyContext);

        var addRow = rows.Single(r => r.Get("Action") is string a && a.StartsWith("HttpAddUrlAcl_"));
        Assert.Equal(3106, addRow.Get("Type"));
    }

    [Fact]
    public void UrlReservation_RollbackRow_IsType3362()
    {
        var reservations = new List<UrlReservationModel> { new("http://+:8080/svc/", "D:(A;;GX;;;NS)") };
        var contributor = new HttpCustomActionContributor(reservations, []);
        var rows = contributor.GetRows(EmptyContext);

        var rollbackRow = rows.Single(r => r.Get("Action") is string a && a.StartsWith("HttpRollbackUrlAcl_"));
        Assert.Equal(3362, rollbackRow.Get("Type"));
    }

    [Fact]
    public void UrlReservation_RollbackRow_UsesDeleteCommand()
    {
        var reservations = new List<UrlReservationModel> { new("http://+:8080/svc/", "D:(A;;GX;;;NS)") };
        var contributor = new HttpCustomActionContributor(reservations, []);
        var rows = contributor.GetRows(EmptyContext);

        var rollbackRow = rows.Single(r => r.Get("Action") is string a && a.StartsWith("HttpRollbackUrlAcl_"));
        var target = (string)rollbackRow.Get("Target")!;
        Assert.Contains("netsh.exe http delete urlacl", target);
    }

    [Fact]
    public void UrlReservation_RemoveRow_UsesDeleteCommand()
    {
        var reservations = new List<UrlReservationModel> { new("http://+:8080/svc/", "D:(A;;GX;;;NS)") };
        var contributor = new HttpCustomActionContributor(reservations, []);
        var rows = contributor.GetRows(EmptyContext);

        var removeRow = rows.Single(r => r.Get("Action") is string a && a.StartsWith("HttpRemoveUrlAcl_"));
        var target = (string)removeRow.Get("Target")!;
        Assert.Contains("netsh.exe http delete urlacl", target);
    }

    [Fact]
    public void OneSniBinding_EmitsThreeRows()
    {
        var appId = Guid.NewGuid();
        var bindings = new List<SniSslBindingModel> { new("api.example.com", 443, ValidThumbprint, appId) };
        var contributor = new HttpCustomActionContributor([], bindings);

        Assert.Equal(3, contributor.GetRows(EmptyContext).Count);
    }

    [Fact]
    public void SniBinding_AddRow_HasCorrectCommand()
    {
        var appId = Guid.NewGuid();
        var bindings = new List<SniSslBindingModel> { new("api.example.com", 443, ValidThumbprint, appId) };
        var contributor = new HttpCustomActionContributor([], bindings);
        var rows = contributor.GetRows(EmptyContext);

        var addRow = rows.Single(r => r.Get("Action") is string a && a.StartsWith("HttpAddSslCert_"));
        var target = (string)addRow.Get("Target")!;
        Assert.Contains("netsh.exe http add sslcert", target);
        Assert.Contains("api.example.com:443", target);
        Assert.Contains(ValidThumbprint, target);
        Assert.Contains(appId.ToString(), target);
    }

    [Fact]
    public void TwoReservations_EmitsSixRows_WithDistinctNames()
    {
        var reservations = new List<UrlReservationModel>
        {
            new("http://+:8080/svc/", "D:(A;;GX;;;NS)"),
            new("http://+:9090/api/", "D:(A;;GX;;;LS)")
        };
        var contributor = new HttpCustomActionContributor(reservations, []);
        var rows = contributor.GetRows(EmptyContext);

        Assert.Equal(6, rows.Count);
        var names = rows.Select(r => (string)r.Get("Action")!).ToHashSet();
        Assert.Equal(6, names.Count); // All distinct
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/FalkForge.Extensions.Http.Tests/Compilation/ -q`
Expected: FAIL — type not found

**Step 3: Implement contributor**

```csharp
// src/FalkForge.Extensions.Http/Compilation/HttpCustomActionContributor.cs
using FalkForge.Extensibility;
using FalkForge.Extensions.Http.Models;

namespace FalkForge.Extensions.Http.Compilation;

internal sealed class HttpCustomActionContributor(
    IReadOnlyList<UrlReservationModel> reservations,
    IReadOnlyList<SniSslBindingModel> bindings) : IMsiTableContributor
{
    // MSI CA type: ExeFile-in-directory (34) + deferred (0x400) + no-impersonate/elevated (0x800)
    private const int TypeDeferred = 3106;
    // MSI CA type: ExeFile-in-directory (34) + rollback (0x100) + deferred (0x400) + no-impersonate (0x800)
    private const int TypeRollback = 3362;

    public string TableName => "CustomAction";

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        var rows = new List<MsiTableRow>();

        for (var i = 0; i < reservations.Count; i++)
        {
            var r = reservations[i];
            var addTarget = $"netsh.exe http add urlacl url=\"{r.Url}\" user=\"{r.User}\"";
            var delTarget = $"netsh.exe http delete urlacl url=\"{r.Url}\"";

            rows.Add(MakeRow($"HttpAddUrlAcl_{i}",      TypeDeferred, addTarget));
            rows.Add(MakeRow($"HttpRollbackUrlAcl_{i}", TypeRollback, delTarget));
            rows.Add(MakeRow($"HttpRemoveUrlAcl_{i}",   TypeDeferred, delTarget));
        }

        for (var i = 0; i < bindings.Count; i++)
        {
            var b = bindings[i];
            var hostnamePort = $"{b.Hostname}:{b.Port}";
            var addTarget = $"netsh.exe http add sslcert hostnameport=\"{hostnamePort}\" certhash={b.CertificateThumbprint} appid={{{b.AppId}}} certstorename={b.CertStoreName}";
            var delTarget = $"netsh.exe http delete sslcert hostnameport=\"{hostnamePort}\"";

            rows.Add(MakeRow($"HttpAddSslCert_{i}",      TypeDeferred, addTarget));
            rows.Add(MakeRow($"HttpRollbackSslCert_{i}", TypeRollback, delTarget));
            rows.Add(MakeRow($"HttpRemoveSslCert_{i}",   TypeDeferred, delTarget));
        }

        return rows;
    }

    private static MsiTableRow MakeRow(string action, int type, string target)
        => new MsiTableRow()
            .Set("Action", action)
            .Set("Type", type)
            .Set("Source", "SystemFolder")
            .Set("Target", target);
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/FalkForge.Extensions.Http.Tests/ -q`
Expected: PASS — all tests

**Step 5: Commit**

```bash
git add src/FalkForge.Extensions.Http/Compilation/HttpCustomActionContributor.cs \
        tests/FalkForge.Extensions.Http.Tests/Compilation/HttpCustomActionContributorTests.cs
git commit -m "feat: add HttpCustomActionContributor generating netsh CAs"
```

---

### Task 6: HttpSequenceContributor

**Files:**
- Create: `src/FalkForge.Extensions.Http/Compilation/HttpSequenceContributor.cs`
- Create: `tests/FalkForge.Extensions.Http.Tests/Compilation/HttpSequenceContributorTests.cs`

**Step 1: Write failing tests**

```csharp
// tests/FalkForge.Extensions.Http.Tests/Compilation/HttpSequenceContributorTests.cs
using FalkForge.Extensibility;
using FalkForge.Extensions.Http.Compilation;
using FalkForge.Extensions.Http.Models;
using FalkForge.Models;

namespace FalkForge.Extensions.Http.Tests.Compilation;

public sealed class HttpSequenceContributorTests
{
    private static readonly ExtensionContext EmptyContext = new()
    {
        Package = new PackageModel
        {
            Name = "Test", Manufacturer = "Test", Version = new Version(1, 0, 0)
        },
        OutputDirectory = "out",
        SourceDirectory = "src"
    };

    private const string ValidThumbprint = "ABCDEF1234567890ABCDEF1234567890ABCDEF12";

    [Fact]
    public void TableName_IsInstallExecuteSequence()
    {
        var contributor = new HttpSequenceContributor([], []);
        Assert.Equal("InstallExecuteSequence", contributor.TableName);
    }

    [Fact]
    public void NoReservationsOrBindings_ReturnsEmpty()
    {
        var contributor = new HttpSequenceContributor([], []);
        Assert.Empty(contributor.GetRows(EmptyContext));
    }

    [Fact]
    public void OneUrlReservation_EmitsThreeSequenceRows()
    {
        var reservations = new List<UrlReservationModel> { new("http://+:8080/svc/", "D:(A;;GX;;;NS)") };
        var contributor = new HttpSequenceContributor(reservations, []);

        Assert.Equal(3, contributor.GetRows(EmptyContext).Count);
    }

    [Fact]
    public void AddRow_HasCondition_NOT_Installed()
    {
        var reservations = new List<UrlReservationModel> { new("http://+:8080/svc/", "D:(A;;GX;;;NS)") };
        var contributor = new HttpSequenceContributor(reservations, []);
        var rows = contributor.GetRows(EmptyContext);

        var addRow = rows.Single(r => r.Get("Action") is string a && a.StartsWith("HttpAddUrlAcl_"));
        Assert.Equal("NOT Installed", addRow.Get("Condition"));
    }

    [Fact]
    public void RemoveRow_HasCondition_Installed()
    {
        var reservations = new List<UrlReservationModel> { new("http://+:8080/svc/", "D:(A;;GX;;;NS)") };
        var contributor = new HttpSequenceContributor(reservations, []);
        var rows = contributor.GetRows(EmptyContext);

        var removeRow = rows.Single(r => r.Get("Action") is string a && a.StartsWith("HttpRemoveUrlAcl_"));
        Assert.Equal("Installed", removeRow.Get("Condition"));
    }

    [Fact]
    public void RollbackRow_SequencedBeforeAddRow()
    {
        var reservations = new List<UrlReservationModel> { new("http://+:8080/svc/", "D:(A;;GX;;;NS)") };
        var contributor = new HttpSequenceContributor(reservations, []);
        var rows = contributor.GetRows(EmptyContext);

        var rollbackSeq = (int)rows.Single(r => r.Get("Action") is string a && a.StartsWith("HttpRollbackUrlAcl_")).Get("Sequence")!;
        var addSeq      = (int)rows.Single(r => r.Get("Action") is string a && a.StartsWith("HttpAddUrlAcl_")).Get("Sequence")!;

        Assert.True(rollbackSeq < addSeq, $"Rollback seq {rollbackSeq} should be before Add seq {addSeq}");
    }

    [Fact]
    public void RemoveRow_SequencedBeforeRemoveFiles()
    {
        var reservations = new List<UrlReservationModel> { new("http://+:8080/svc/", "D:(A;;GX;;;NS)") };
        var contributor = new HttpSequenceContributor(reservations, []);
        var rows = contributor.GetRows(EmptyContext);

        var removeSeq = (int)rows.Single(r => r.Get("Action") is string a && a.StartsWith("HttpRemoveUrlAcl_")).Get("Sequence")!;

        Assert.True(removeSeq < 3700, $"Remove seq {removeSeq} should be before RemoveFiles (~3700)");
    }

    [Fact]
    public void SniBinding_EmitsThreeSequenceRows()
    {
        var bindings = new List<SniSslBindingModel>
        {
            new("api.example.com", 443, ValidThumbprint, Guid.NewGuid())
        };
        var contributor = new HttpSequenceContributor([], bindings);

        Assert.Equal(3, contributor.GetRows(EmptyContext).Count);
    }

    [Fact]
    public void MixedItems_SequenceNumbersDoNotCollide()
    {
        var reservations = new List<UrlReservationModel>
        {
            new("http://+:8080/svc/", "D:(A;;GX;;;NS)"),
            new("http://+:9090/api/", "D:(A;;GX;;;LS)")
        };
        var bindings = new List<SniSslBindingModel>
        {
            new("api.example.com", 443, ValidThumbprint, Guid.NewGuid())
        };
        var contributor = new HttpSequenceContributor(reservations, bindings);
        var rows = contributor.GetRows(EmptyContext);

        var sequences = rows.Select(r => (int)r.Get("Sequence")!).ToList();
        Assert.Equal(sequences.Count, sequences.Distinct().Count()); // All unique
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/FalkForge.Extensions.Http.Tests/Compilation/HttpSequenceContributorTests.cs -q`
Expected: FAIL — type not found

**Step 3: Implement contributor**

```csharp
// src/FalkForge.Extensions.Http/Compilation/HttpSequenceContributor.cs
using FalkForge.Extensibility;
using FalkForge.Extensions.Http.Models;

namespace FalkForge.Extensions.Http.Compilation;

internal sealed class HttpSequenceContributor(
    IReadOnlyList<UrlReservationModel> reservations,
    IReadOnlyList<SniSslBindingModel> bindings) : IMsiTableContributor
{
    // Add deferred CAs run just after InstallFiles (sequence ~4100)
    private const int AddBase      = 4150;
    // Rollback CAs must be scheduled BEFORE their deferred twins
    private const int RollbackBase = 4050;
    // Remove CAs run just before RemoveFiles (sequence ~3700)
    private const int RemoveBase   = 3650;

    public string TableName => "InstallExecuteSequence";

    public IReadOnlyList<MsiTableRow> GetRows(ExtensionContext context)
    {
        var rows = new List<MsiTableRow>();
        var offset = 0;

        for (var i = 0; i < reservations.Count; i++, offset++)
        {
            rows.Add(Row($"HttpRollbackUrlAcl_{i}", "NOT Installed", RollbackBase + offset));
            rows.Add(Row($"HttpAddUrlAcl_{i}",      "NOT Installed", AddBase      + offset));
            rows.Add(Row($"HttpRemoveUrlAcl_{i}",   "Installed",     RemoveBase   + offset));
        }

        for (var i = 0; i < bindings.Count; i++, offset++)
        {
            rows.Add(Row($"HttpRollbackSslCert_{i}", "NOT Installed", RollbackBase + offset));
            rows.Add(Row($"HttpAddSslCert_{i}",      "NOT Installed", AddBase      + offset));
            rows.Add(Row($"HttpRemoveSslCert_{i}",   "Installed",     RemoveBase   + offset));
        }

        return rows;
    }

    private static MsiTableRow Row(string action, string condition, int sequence)
        => new MsiTableRow()
            .Set("Action",    action)
            .Set("Condition", condition)
            .Set("Sequence",  sequence);
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/FalkForge.Extensions.Http.Tests/ -q`
Expected: PASS — all tests

**Step 5: Commit**

```bash
git add src/FalkForge.Extensions.Http/Compilation/HttpSequenceContributor.cs \
        tests/FalkForge.Extensions.Http.Tests/Compilation/HttpSequenceContributorTests.cs
git commit -m "feat: add HttpSequenceContributor for InstallExecuteSequence rows"
```

---

### Task 7: HttpExtension

**Files:**
- Create: `src/FalkForge.Extensions.Http/HttpExtension.cs`
- Create: `tests/FalkForge.Extensions.Http.Tests/HttpExtensionTests.cs`

**Step 1: Write failing tests**

```csharp
// tests/FalkForge.Extensions.Http.Tests/HttpExtensionTests.cs
namespace FalkForge.Extensions.Http.Tests;

public sealed class HttpExtensionTests
{
    [Fact]
    public void Name_IsHttp()
    {
        var ext = new HttpExtension();
        Assert.Equal("Http", ext.Name);
    }

    [Fact]
    public void AddUrlReservation_AddsToInternalList()
    {
        var ext = new HttpExtension();
        ext.AddUrlReservation("http://+:8080/svc/", b => b.AllowNetworkService());

        var errors = ext.Validate();
        Assert.Empty(errors);
    }

    [Fact]
    public void AddUrlReservation_ReturnsExtensionForChaining()
    {
        var ext = new HttpExtension();
        var result = ext.AddUrlReservation("http://+:8080/svc/", b => b.AllowNetworkService());

        Assert.Same(ext, result);
    }

    [Fact]
    public void AddSniSslBinding_AddsToInternalList()
    {
        const string thumbprint = "ABCDEF1234567890ABCDEF1234567890ABCDEF12";
        var ext = new HttpExtension();
        ext.AddSniSslBinding("api.example.com", 443, b => b.Thumbprint(thumbprint));

        var errors = ext.Validate();
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_InvalidReservation_ReturnsErrors()
    {
        var ext = new HttpExtension();
        ext.AddUrlReservation("ftp://invalid", b => b.AllowNetworkService());

        var errors = ext.Validate();
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Register_RegistersTwoTableContributors()
    {
        var ext = new HttpExtension();
        var registry = new SpyExtensionRegistry();

        ext.Register(registry);

        Assert.Equal(2, registry.TableContributors.Count);
    }

    [Fact]
    public void Register_RegistersCustomActionAndSequenceContributors()
    {
        var ext = new HttpExtension();
        var registry = new SpyExtensionRegistry();

        ext.Register(registry);

        Assert.Contains(registry.TableContributors, c => c.TableName == "CustomAction");
        Assert.Contains(registry.TableContributors, c => c.TableName == "InstallExecuteSequence");
    }
}

// Test double — spy implementation of IExtensionRegistry
internal sealed class SpyExtensionRegistry : IExtensionRegistry
{
    public List<IMsiTableContributor> TableContributors { get; } = [];

    public void RegisterTableContributor(IMsiTableContributor contributor)
        => TableContributors.Add(contributor);

    public void RegisterComponentContributor(IComponentContributor contributor) { }
    public void RegisterValidator(IExtensionValidator validator) { }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/FalkForge.Extensions.Http.Tests/HttpExtensionTests.cs -q`
Expected: FAIL — type not found

**Step 3: Implement extension**

```csharp
// src/FalkForge.Extensions.Http/HttpExtension.cs
using System.Runtime.Versioning;
using FalkForge;
using FalkForge.Extensibility;
using FalkForge.Extensions.Http.Builders;
using FalkForge.Extensions.Http.Compilation;
using FalkForge.Extensions.Http.Models;
using FalkForge.Extensions.Http.Validation;

namespace FalkForge.Extensions.Http;

[SupportedOSPlatform("windows")]
public sealed class HttpExtension : IFalkForgeExtension
{
    private readonly List<UrlReservationModel> _reservations = [];
    private readonly List<SniSslBindingModel>  _bindings     = [];

    public string Name => "Http";

    public HttpExtension AddUrlReservation(string url, Action<UrlReservationBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        var builder = new UrlReservationBuilder(url);
        configure(builder);
        _reservations.Add(builder.Build());
        return this;
    }

    public HttpExtension AddSniSslBinding(string hostname, int port, Action<SniSslBindingBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostname);
        var builder = new SniSslBindingBuilder(hostname, port);
        configure(builder);
        _bindings.Add(builder.Build());
        return this;
    }

    public IReadOnlyList<Error> Validate()
    {
        var errors = new List<Error>();
        errors.AddRange(HttpValidator.ValidateReservations(_reservations));
        errors.AddRange(HttpValidator.ValidateBindings(_bindings));
        return errors;
    }

    public void Register(IExtensionRegistry registry)
    {
        registry.RegisterTableContributor(new HttpCustomActionContributor(_reservations, _bindings));
        registry.RegisterTableContributor(new HttpSequenceContributor(_reservations, _bindings));
    }
}
```

**Step 4: Run tests to verify they pass**

Run: `dotnet test tests/FalkForge.Extensions.Http.Tests/ -q`
Expected: PASS — all tests

**Step 5: Run full test suite**

Run: `dotnet test -q`
Expected: All previously-passing tests still pass. New Http.Tests project passes.

**Step 6: Commit**

```bash
git add src/FalkForge.Extensions.Http/HttpExtension.cs tests/FalkForge.Extensions.Http.Tests/HttpExtensionTests.cs
git commit -m "feat: add HttpExtension wiring URL reservations and SNI SSL bindings"
```
