using FalkForge;
using FalkForge.Compiler.Msi;
using FalkForge.Extensions.Http;

// HTTP extension: URL reservation and SNI SSL certificate binding.
var http = new HttpExtension();

// --- URL reservation for a web service ---
// Reserves http://+:8080/api/ so the service can listen without admin privileges.
http.AddUrlReservation("http://+:8080/api/", url =>
    url.AllowNetworkService());

// --- URL reservation for a management endpoint ---
// Allows all built-in users to listen on the management port.
http.AddUrlReservation("http://+:9090/admin/", url =>
    url.AllowBuiltinUsers());

// --- SNI SSL certificate binding ---
// Binds an SSL certificate to a specific hostname for HTTPS traffic.
http.AddSniSslBinding("api.example.com", 443, ssl =>
{
    ssl.Thumbprint("0123456789ABCDEF0123456789ABCDEF01234567");
    ssl.CertStoreName("MY");
});

// --- Second SSL binding for management endpoint ---
http.AddSniSslBinding("admin.example.com", 443, ssl =>
    ssl.Thumbprint("FEDCBA9876543210FEDCBA9876543210FEDCBA98"));

// Validate all reservations and bindings
var validation = http.Validate();
if (validation.IsFailure)
{
    Console.Error.WriteLine($"HTTP validation failed: {validation.Error}");
    return 1;
}

Console.WriteLine("HTTP extension: 2 URL reservations, 2 SSL bindings configured.");

// In production, extensions register automatically via the FalkForge SDK extension
// pipeline during compilation. The package below shows the MSI structure; extension
// tables are emitted by the SDK at build time.
return Installer.Build(args, package =>
{
    package.Name = "HTTP Extension Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.Files(files => files
        .Add("payload/apiservice.exe")
        .To(KnownFolder.ProgramFiles / "Demo" / "HttpDemo"));
}, new MsiCompiler());
