# HTTP Extension Design

**Date:** 2026-02-25
**Status:** Design
**Scope:** `Extensions.Http` — URL ACL reservations and SNI SSL bindings via `netsh`, implemented as MSI custom actions contributed through `IMsiTableContributor`

---

## Current State

No HTTP extension exists. URL reservations and SNI SSL bindings for non-IIS scenarios (WCF services, self-hosted .NET, kestrel with elevated binding) must be set up manually by consumers. IIS bindings are handled by `Extensions.Iis` via `Microsoft.Web.Administration`, but URL reservations and SNI SSL are OS-level operations independent of IIS.

---

## Design

### 1. Project Structure

New project: `src/FalkForge.Extensions.Http/`

```
Extensions.Http/
  HttpExtension.cs                    Entry point, IFalkForgeExtension
  Models/
    UrlReservationModel.cs            Url + User (SDDL string)
    SniSslBindingModel.cs             Hostname, Port, CertificateThumbprint, AppId, CertStoreName
  Builders/
    UrlReservationBuilder.cs          Fluent builder with SDDL helpers
    SniSslBindingBuilder.cs           Fluent builder
  Compilation/
    HttpTableContributor.cs           IMsiTableContributor → CustomAction + InstallExecuteSequence rows
  Validation/
    HttpValidator.cs                  IExtensionValidator, HTTP001–HTTP009
```

Test project: `tests/FalkForge.Extensions.Http.Tests/`

**Targeting:** `net10.0-windows` (netsh is Windows-only; guarded with `[SupportedOSPlatform("windows")]`)

---

### 2. Models

**`UrlReservationModel`**

| Property | Type   | Description                                      |
|----------|--------|--------------------------------------------------|
| `Url`    | string | Full URL prefix, e.g. `http://+:8080/myservice/` |
| `User`   | string | SDDL string for the ACL grant                    |

**`SniSslBindingModel`**

| Property                | Type   | Description                                              |
|-------------------------|--------|----------------------------------------------------------|
| `Hostname`              | string | SNI hostname, e.g. `api.example.com`                     |
| `Port`                  | int    | TCP port (typically 443)                                 |
| `CertificateThumbprint` | string | 40-char hex SHA-1 thumbprint                             |
| `AppId`                 | Guid   | Application GUID (auto-derived from hostname:port SHA-256 if not set) |
| `CertStoreName`         | string | Certificate store, default `MY`                          |

---

### 3. Builder API

**`UrlReservationBuilder`** — SDDL helpers for well-known accounts:

```csharp
public UrlReservationBuilder AllowNetworkService()  // D:(A;;GX;;;NS)
public UrlReservationBuilder AllowLocalService()    // D:(A;;GX;;;LS)
public UrlReservationBuilder AllowLocalSystem()     // D:(A;;GX;;;SY)
public UrlReservationBuilder AllowEveryone()        // D:(A;;GX;;;WD)
public UrlReservationBuilder AllowBuiltinUsers()    // D:(A;;GX;;;BU)
public UrlReservationBuilder AllowUser(string user) // Caller provides full SDDL or account name
```

**`SniSslBindingBuilder`**

```csharp
public SniSslBindingBuilder Thumbprint(string thumbprint)
public SniSslBindingBuilder AppId(Guid appId)           // Optional; auto-derived if omitted
public SniSslBindingBuilder Hostname(string hostname)
public SniSslBindingBuilder Port(int port)
public SniSslBindingBuilder CertStoreName(string store) // Default: MY
```

**`HttpExtension`** — entry point registered via `PackageBuilder.UseExtension<HttpExtension>()`:

```csharp
public HttpExtension AddUrlReservation(string url, Action<UrlReservationBuilder> configure)
public HttpExtension AddSniSslBinding(string hostname, int port, Action<SniSslBindingBuilder> configure)
```

---

### 4. Implementation — `HttpTableContributor`

Implements `IMsiTableContributor`. Contributes rows to `CustomAction` and `InstallExecuteSequence` tables.

**Per URL reservation — three CAs:**

| Action name                    | Type | Source (command)                                          | Condition          |
|--------------------------------|------|-----------------------------------------------------------|--------------------|
| `HttpAddUrlAcl_{n}`            | 34   | `[SystemFolder]netsh.exe http add urlacl url="{url}" user="{sddl}"` | `NOT Installed`    |
| `HttpRollbackUrlAcl_{n}`       | 34   | `[SystemFolder]netsh.exe http delete urlacl url="{url}"`  | Rollback, `NOT Installed` |
| `HttpRemoveUrlAcl_{n}`         | 34   | `[SystemFolder]netsh.exe http delete urlacl url="{url}"`  | `Installed`        |

**Per SNI SSL binding — three CAs:**

| Action name                    | Type | Source (command)                                                                    | Condition          |
|--------------------------------|------|-------------------------------------------------------------------------------------|--------------------|
| `HttpAddSslCert_{n}`           | 34   | `[SystemFolder]netsh.exe http add sslcert hostnameport="{host}:{port}" certhash="{thumb}" appid="{guid}" certstorename="{store}"` | `NOT Installed`    |
| `HttpRollbackSslCert_{n}`      | 34   | `[SystemFolder]netsh.exe http delete sslcert hostnameport="{host}:{port}"`          | Rollback, `NOT Installed` |
| `HttpRemoveSslCert_{n}`        | 34   | `[SystemFolder]netsh.exe http delete sslcert hostnameport="{host}:{port}"`          | `Installed`        |

**Sequence positions:**
- Add actions → after `InstallFiles`
- Remove actions → before `RemoveFiles`

CA type 34: deferred, impersonate=false (runs elevated). `[SystemFolder]` resolves to `C:\Windows\System32\` at install time.

**AppId auto-derivation:** SHA-256 of UTF-8 `"{hostname}:{port}"`, first 16 bytes interpreted as `Guid` (big-endian, version/variant bits not set — deterministic, not RFC 4122). Stable across compile runs for the same input.

---

### 5. Validation — `HttpValidator`

| Code   | Rule                                                              |
|--------|-------------------------------------------------------------------|
| HTTP001 | `UrlReservationModel.Url` is null or empty                       |
| HTTP002 | `Url` does not start with `http://` or `https://` (case-insensitive) |
| HTTP003 | `Url` does not end with `/`                                       |
| HTTP004 | `UrlReservationModel.User` is null or empty                      |
| HTTP005 | `SniSslBindingModel.Hostname` is null or empty                   |
| HTTP006 | `SniSslBindingModel.Port` is outside 1–65535                     |
| HTTP007 | `SniSslBindingModel.CertificateThumbprint` is null or empty      |
| HTTP008 | `CertificateThumbprint` is not exactly 40 hexadecimal characters |
| HTTP009 | `SniSslBindingModel.AppId` is not a valid GUID (raw model only)  |

`HttpValidator` registered via `HttpExtension.Register(IExtensionRegistry registry)`:

```csharp
public void Register(IExtensionRegistry registry)
{
    registry.AddValidator(new HttpValidator(this));
    registry.AddTableContributor(new HttpTableContributor(this));
}
```

Unlike `IisExtension` (which is model-only at compile time; DLL CA runs at install time), `HttpExtension` generates MSI table rows directly so `Register()` is non-empty.

---

### 6. Testing Strategy

**`HttpValidatorTests`**
- One failing test per error code HTTP001–HTTP009
- Happy-path: valid URL reservation + valid SNI binding both pass

**`HttpTableContributorTests`**
- Three CAs generated per URL reservation with correct `netsh http add urlacl` / `delete urlacl` command text
- Three CAs generated per SNI SSL binding with correct `netsh http add sslcert` / `delete sslcert` command text
- Sequence positions: Add after `InstallFiles`, Remove before `RemoveFiles`
- Uses in-memory `MsiTableRow` list (same pattern as Iis tests)

**`UrlReservationBuilderTests`**
- Each SDDL helper maps to the correct SDDL string constant

**`SniSslBindingBuilderTests`**
- AppId auto-derived from hostname+port is a valid GUID and deterministic

**`HttpExtensionTests`**
- `Register()` adds validator and contributor to registry

---

## Error Codes Summary

| Code   | Description                                         |
|--------|-----------------------------------------------------|
| HTTP001 | URL is null or empty                               |
| HTTP002 | URL does not start with http:// or https://        |
| HTTP003 | URL does not end with /                            |
| HTTP004 | User/SDDL string is null or empty                  |
| HTTP005 | SNI hostname is null or empty                      |
| HTTP006 | Port is outside 1–65535                            |
| HTTP007 | Certificate thumbprint is null or empty            |
| HTTP008 | Certificate thumbprint is not 40 hex characters    |
| HTTP009 | AppId is not a valid GUID                          |
| HTTP010 | (reserved for future use)                          |
