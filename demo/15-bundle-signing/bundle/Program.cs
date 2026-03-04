using FalkForge;
using FalkForge.Compiler.Bundle;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;

// Demo 15 -- Bundle Signing
//
// Demonstrates the full code-signing workflow for FALKBUNDLE EXEs:
//   1. Compile a bundle wrapping an MSI package
//   2. Detach the PE stub from the bundle data
//   3. Sign the PE stub (placeholder — insert your signtool call here)
//   4. Reattach the signed stub with the bundle data
//
// The detach/reattach process preserves all payload offsets, patching
// the TOC to account for stub size changes introduced by Authenticode.

// With FalkForge SDK source generation, use: ProjectOutputs.MsiPackage
// For standalone demo, reference built MSI paths directly:
const string msiPath = "../msi-package/msi-package.msi";

return Installer.BuildBundle(args, outputPath =>
{
    // ──────────────────────────────────────────────────────────────────
    // 1. Build the bundle model
    // ──────────────────────────────────────────────────────────────────
    var bundle = new BundleBuilder()
        .Name("Signing Demo Bundle")
        .Manufacturer("FalkForge Demo")
        .Version("1.0.0")
        .BundleId(new Guid("1A2B3C4D-5E6F-4A7B-8C9D-0E1F2A3B4C5D"))
        .UpgradeCode(new Guid("2B3C4D5E-6F7A-4B8C-9D0E-1F2A3B4C5D6E"))
        .Scope(InstallScope.PerMachine)
        .Chain(chain => chain
            .MsiPackage(msiPath, msi => msi
                .Id("SigningDemoApp")
                .DisplayName("Signing Demo Application")
                .Version("1.0.0")
                .Vital(true)))
        .Build();

    // ──────────────────────────────────────────────────────────────────
    // 2. Compile the bundle to a self-extracting EXE
    // ──────────────────────────────────────────────────────────────────
    var compiler = new BundleCompiler();
    var compileResult = compiler.Compile(bundle, outputPath);
    if (!compileResult.IsSuccess)
        return compileResult;

    Console.WriteLine($"Bundle compiled: {outputPath}");

    // ──────────────────────────────────────────────────────────────────
    // 3. Detach: split into bare PE stub + data file
    // ──────────────────────────────────────────────────────────────────
    var stubPath = Path.ChangeExtension(outputPath, ".stub.exe");
    var dataPath = Path.ChangeExtension(outputPath, ".data");

    var detachResult = BundleDetacher.Detach(outputPath, stubPath, dataPath);
    if (!detachResult.IsSuccess)
        return Result<string>.Failure(detachResult.Error.Kind, detachResult.Error.Message);

    Console.WriteLine($"Detached stub: {stubPath}");
    Console.WriteLine($"Detached data: {dataPath}");

    // ──────────────────────────────────────────────────────────────────
    // 4. Sign the PE stub (placeholder)
    //
    // In a real pipeline, call signtool or your signing service here:
    //   signtool sign /fd SHA256 /a /tr http://timestamp.example.com stub.exe
    //
    // For this demo, we skip actual signing and reattach the unsigned
    // stub to verify the workflow end-to-end.
    // ──────────────────────────────────────────────────────────────────
    Console.WriteLine("(Signing step placeholder — insert signtool invocation here)");

    // ──────────────────────────────────────────────────────────────────
    // 5. Reattach: combine signed stub + data → final signed bundle
    // ──────────────────────────────────────────────────────────────────
    var signedOutputPath = Path.ChangeExtension(outputPath, ".signed.exe");

    var reattachResult = BundleDetacher.Reattach(stubPath, dataPath, signedOutputPath);
    if (!reattachResult.IsSuccess)
        return Result<string>.Failure(reattachResult.Error.Kind, reattachResult.Error.Message);

    Console.WriteLine($"Reattached signed bundle: {signedOutputPath}");

    // ──────────────────────────────────────────────────────────────────
    // Clean up intermediate files
    // ──────────────────────────────────────────────────────────────────
    File.Delete(stubPath);
    File.Delete(dataPath);
    Console.WriteLine("Intermediate files cleaned up.");

    return compileResult;
});
