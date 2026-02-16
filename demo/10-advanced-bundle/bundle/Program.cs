using FalkForge;
using FalkForge.Compiler.Bundle;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;

// Demo 10 -- Advanced Bundle
//
// Demonstrates the full FalkForge bundle API:
//   - Multiple package types: ExePackage, MsiPackage, MsuPackage, MspPackage
//   - Exit code mapping for non-MSI packages
//   - Install conditions (conditional package execution)
//   - Related bundles (upgrade detection)
//   - Rollback boundaries (isolate failure domains)
//   - Named containers (logical payload grouping)
//   - Built-in UI with theming

// With FalkForge SDK source generation, use: ProjectOutputs.MsiPackage
// For standalone demo, reference built MSI paths directly:
const string northwindMsiPath = "../msi-package/msi-package.msi";

var payloadDir = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "payload"));

var prereqExePath = Path.Combine(payloadDir, "prereq.exe");
var hotfixMsuPath = Path.Combine(payloadDir, "hotfix.msu");
var patchMspPath = Path.Combine(payloadDir, "patch.msp");

return Installer.BuildBundle(args, outputPath =>
{
    // ──────────────────────────────────────────────────────────────────
    // Build the bundle model
    // ──────────────────────────────────────────────────────────────────
    var bundleBuilder = new BundleBuilder()
        .Name("Northwind Deployment Suite")
        .Manufacturer("Northwind Traders")
        .Version("2.5.0")
        .BundleId(new Guid("A1B2C3D4-E5F6-4A7B-8C9D-0E1F2A3B4C5D"))
        .UpgradeCode(new Guid("B2C3D4E5-F6A7-4B8C-9D0E-1F2A3B4C5D6E"))
        .Scope(InstallScope.PerMachine)

        // ──────────────────────────────────────────────────────────────
        // Built-in UI with branding
        // ──────────────────────────────────────────────────────────────
        .UseBuiltInUI(themeColor: "#1E40AF")

        // ──────────────────────────────────────────────────────────────
        // Related bundles -- detect and upgrade previous versions
        // ──────────────────────────────────────────────────────────────
        .RelatedBundle(new Guid("C3D4E5F6-A7B8-4C9D-0E1F-2A3B4C5D6E7F"), rb => rb
            .Relation(RelatedBundleRelation.Upgrade))
        .RelatedBundle(new Guid("D4E5F6A7-B8C9-4D0E-1F2A-3B4C5D6E7F8A"), rb => rb
            .Relation(RelatedBundleRelation.Detect));

    // ──────────────────────────────────────────────────────────────
    // Containers -- typed references for logical payload grouping
    // ──────────────────────────────────────────────────────────────
    var prereqs = bundleBuilder.DefineContainer("Prerequisites", c => c
        .DownloadUrl("https://cdn.northwind.example.com/prereqs/"));
    var appContainer = bundleBuilder.DefineContainer("Application", c => c
        .DownloadUrl("https://cdn.northwind.example.com/app/"));

    // ──────────────────────────────────────────────────────────────
    // Rollback boundaries -- typed references for failure isolation
    // ──────────────────────────────────────────────────────────────
    var prereqBoundary = bundleBuilder.DefineRollbackBoundary("PrereqBoundary");
    var appBoundary = bundleBuilder.DefineRollbackBoundary("AppBoundary");

    // ──────────────────────────────────────────────────────────────
    // Installation chain
    // ──────────────────────────────────────────────────────────────
    var bundle = bundleBuilder
        .Chain(chain => chain

            // Rollback boundary: prerequisites
            // If any prerequisite fails, only prerequisites roll back.
            .RollbackBoundary(prereqBoundary)

            // EXE package: Visual C++ Redistributable prerequisite
            .ExePackage(prereqExePath, exe => exe
                .Id("VCRedist")
                .DisplayName("Visual C++ Redistributable")
                .Version("14.40.33816")
                .Vital(true)
                .InstallCondition(!Condition.Property("VCRedistInstalled"))
                .Container(prereqs)
                .ExitCode(0, ExitCodeBehavior.Success)
                .ExitCode(3010, ExitCodeBehavior.RebootRequired)
                .ExitCode(1638, ExitCodeBehavior.Success)    // Already installed
                .ExitCode(1602, ExitCodeBehavior.Failure))   // User cancelled

            // MSU package: Windows security hotfix
            .MsuPackage(hotfixMsuPath, msu => msu
                .Id("SecurityHotfix")
                .DisplayName("Security Update KB5034441")
                .KbArticle("KB5034441")
                .Vital(false)
                .InstallCondition(Condition.IsWindows10OrLater & !Condition.Property("KB5034441Installed")))

            // Rollback boundary: application
            // Isolates the main application from prerequisites.
            .RollbackBoundary(appBoundary, rb => rb
                .Vital(true))

            // MSI package: the Northwind application
            .MsiPackage(northwindMsiPath, msi => msi
                .Id("NorthwindApp")
                .DisplayName("Northwind Application")
                .Version("2.5.0")
                .Vital(true)
                .Container(appContainer)
                .Property("INSTALLFOLDER", (MsiProperty.ProgramFiles64Folder / @"Northwind Traders\NorthwindApp").ToString())
                .Property("NORTHWIND_MODE", "bundled"))

            // MSP package: cumulative patch (applied after MSI install)
            .MspPackage(patchMspPath, msp => msp
                .Id("NorthwindPatch")
                .DisplayName("Northwind Application Patch v2.5.1")
                .PatchCode("{E5F6A7B8-C9D0-4E1F-2A3B-4C5D6E7F8A9B}")
                .TargetProductCode("{D1E2F3A4-B5C6-4D7E-8F9A-0B1C2D3E4F5A}")
                .Vital(false)
                .InstallCondition(Condition.Property("PATCH_AVAILABLE"))))

        .Build();

    // ──────────────────────────────────────────────────────────────────
    // Compile the bundle to a self-extracting EXE bootstrapper
    // ──────────────────────────────────────────────────────────────────
    var compiler = new BundleCompiler();
    return compiler.Compile(bundle, outputPath);
});
