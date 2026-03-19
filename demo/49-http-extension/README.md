# Demo 49: HTTP Extension

Configures HTTP URL ACL reservations and SNI SSL certificate bindings as part of an MSI installer, using the
`HttpExtension` API with built-in validation.

## What This Demonstrates

- Creating an `HttpExtension` instance and adding URL reservations
- `AllowNetworkService()` and `AllowBuiltinUsers()` SDDL shortcuts for URL ACLs
- SNI SSL certificate binding with `Thumbprint()` and `CertStoreName()`
- Automatic `AppId` derivation from hostname and port
- Validating all HTTP configuration before compilation with `Validate()`

## Key API Calls

```csharp
var http = new HttpExtension();

// URL reservation — allows NetworkService to listen on port 8080
http.AddUrlReservation("http://+:8080/api/", url =>
    url.AllowNetworkService());

// SNI SSL binding — binds certificate to hostname for HTTPS
http.AddSniSslBinding("api.example.com", 443, ssl =>
{
    ssl.Thumbprint("0123456789ABCDEF0123456789ABCDEF01234567");
    ssl.CertStoreName("MY");
});

var validation = http.Validate();
```

## How to Build

```bash
dotnet build demo/49-http-extension
```

## Notes

- URL reservations use `netsh http add urlacl` under the hood, executed as deferred custom actions.
- SSL bindings use `netsh http add sslcert` with SNI support for hostname-based certificate selection.
- If no `AppId` is specified, one is automatically derived from the hostname and port using SHA-256.
- The `AllowNetworkService()`, `AllowLocalService()`, `AllowLocalSystem()`, `AllowEveryone()`, and
  `AllowBuiltinUsers()` methods are convenience wrappers around SDDL strings.
- URL reservations and SSL bindings are removed on uninstall.
