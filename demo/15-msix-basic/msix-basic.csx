#r "nuget: FalkForge.Core"
#r "nuget: FalkForge.Compiler.Msix"

using FalkForge;
using FalkForge.Compiler.Msix;
using FalkForge.Compiler.Msix.Builders;

InstallerMsix.BuildMsix(Args.ToArray(), msix =>
{
    msix
        .Name("FalkForge.Demo.MsixBasic")
        .Publisher("CN=FalkForge Demo")
        .DisplayName("MSIX Basic Demo")
        .PublisherDisplayName("FalkForge")
        .Version(new Version(1, 0, 0, 0))
        .Architecture(ProcessorArchitecture.X64)
        .Application("App", "hello.exe", app => app
            .DisplayName("Hello World")
            .Square44x44Logo("assets/Square44x44Logo.png")
            .Square150x150Logo("assets/Square150x150Logo.png"))
        .Capability("internetClient")
        .MinWindowsVersion("10.0.17763.0")
        .Signing(s => s.CertificatePath("demo-cert.pfx"));
}, (model, outputPath) =>
{
    var compiler = new MsixCompiler();
    return compiler.Compile(model, outputPath);
});
