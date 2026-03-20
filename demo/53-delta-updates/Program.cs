using FalkForge;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;

// Demo: Delta Bundle Updates
//
// Delta bundles contain only binary diffs of changed payloads, dramatically
// reducing update download size. This demo builds a v1 base bundle and then
// a v2 delta bundle that references v1.
//
// In production, you would build v1 and v2 in separate CI runs. This demo
// builds both sequentially to illustrate the full workflow.

// ── Step 1: Build the v1 (base) bundle ──────────────────────────────────

return Installer.BuildBundle(args, outputPath =>
{
    var v1Bundle = new BundleBuilder()
        .Name("DeltaDemo")
        .Manufacturer("Demo")
        .Version("1.0.0")
        .BundleId(new Guid("A1B2C3D4-E5F6-4A5B-8C9D-E0F1A2B3C4D5"))
        .UpgradeCode(new Guid("B2C3D4E5-F6A7-4B8C-9D0E-1F2A3B4C5D6E"))
        .Scope(InstallScope.PerMachine)
        .UseBuiltInUI()
        .UpdateFeed("https://updates.example.com/deltademo/feed.json")
        .Chain(chain => chain
            .MsiPackage("MyApp.msi", p => p
                .Id("MyApp")
                .DisplayName("My Application")
                .Vital(true)))
        .Build();

    var v1Result = new BundleCompiler().Compile(v1Bundle, outputPath);
    if (v1Result.IsFailure)
        return v1Result;

    var v1Path = v1Result.Value;
    Console.WriteLine($"v1 bundle: {v1Path}");

    // ── Step 2: Build the v2 delta bundle referencing v1 ────────────────

    var v2Bundle = new BundleBuilder()
        .Name("DeltaDemo")
        .Manufacturer("Demo")
        .Version("2.0.0")
        .BundleId(new Guid("C3D4E5F6-A7B8-4C9D-0E1F-2A3B4C5D6E7F"))
        .UpgradeCode(new Guid("B2C3D4E5-F6A7-4B8C-9D0E-1F2A3B4C5D6E"))
        .Scope(InstallScope.PerMachine)
        .UseBuiltInUI()
        .DeltaFrom(v1Path)
        .UpdateFeed("https://updates.example.com/deltademo/feed.json")
        .Chain(chain => chain
            .MsiPackage("MyApp.msi", p => p
                .Id("MyApp")
                .DisplayName("My Application")
                .Vital(true)))
        .Build();

    var v2Result = new DeltaBundleCompiler().Compile(v2Bundle, outputPath, v1Path);
    if (v2Result.IsFailure)
        return v2Result;

    var v2Path = v2Result.Value;
    Console.WriteLine($"v2 delta bundle: {v2Path}");

    // ── Step 3: Compare sizes ───────────────────────────────────────────

    var v1Size = new FileInfo(v1Path).Length;
    var v2Size = new FileInfo(v2Path).Length;
    var savings = v1Size > 0 ? (1.0 - (double)v2Size / v1Size) * 100 : 0;

    Console.WriteLine();
    Console.WriteLine($"v1 full bundle:  {v1Size,10:N0} bytes");
    Console.WriteLine($"v2 delta bundle: {v2Size,10:N0} bytes");
    Console.WriteLine($"Size reduction:  {savings:F1}%");

    return v2Result;
});
