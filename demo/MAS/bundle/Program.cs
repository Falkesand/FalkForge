using FalkForge;
using FalkForge.Compiler.Bundle.Builders;
using FalkForge.Compiler.Bundle.Compilation;

// ---------------------------------------------------------------------------
// MultiAccess Suite — EXE bundle
//
// Mirrors the WiX Bootstrapper/Bundle.wxs chain:
//   1. Prerequisites:  NetFx472, VCRedist, ODBC Driver 17, SQL Express 2017
//   2. Product MSIs:   MultiAccess, MultiServer, Concatenate, Konfigurera
//   3. Post-install:   DatabaseSetup EXE, OdbcSetup EXE
// ---------------------------------------------------------------------------

var packagesDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "packages"));
var stubExe = Path.Combine(packagesDir, "stub", "bin", "Release", "net10.0", "win-x64", "publish", "Stub.exe");

string MsiPath(string name) => Path.Combine(packagesDir, name, "bin", "Release", $"{name}-8.9.0.msi");

return Installer.BuildBundle(args, outputPath =>
{
    var bundle = new BundleBuilder()
        .Name("MultiAccess")
        .Manufacturer("ASSA ABLOY Opening Solutions Sweden AB")
        .Version("8.9.0")
        .BundleId(new Guid("10203040-5060-4070-8090-A0B0C0D0E0F0"))
        .UpgradeCode(new Guid("112B743D-457E-4F28-9CF1-3A1C28FB1F6D"))
        .Scope(InstallScope.PerMachine)
        .UseCustomUI("../MAS.csproj")

        // -----------------------------------------------------------------
        // Bundle variables — match WiX <Variable> declarations
        // -----------------------------------------------------------------

        // Product selection flags
        .Variable("MULTIACCESS", v => v.String().Default("true"))
        .Variable("MULTISERVER", v => v.String().Default("true"))
        .Variable("ASSERVICE", v => v.String().Default("false"))
        .Variable("INSTALLSQL", v => v.String().Default("false"))
        .Variable("KONFIGURERA", v => v.String().Default("false"))
        .Variable("CONCATENATE", v => v.String().Default("false"))
        .Variable("INSTALLDB", v => v.String().Default("true"))
        .Variable("ATTACHDATABASE", v => v.String().Default("false"))

        // Service credentials
        .Variable("SERVICEPASSWORD", v => v.String().Default("false").Secret())
        .Variable("SERVICEACCOUNT", v => v.String().Default("false"))
        .Variable("ODBCNAME", v => v.String().Default(""))

        // Database file locations (default to [DBFOLDER])
        .Variable("DB_MDFLOCATION", v => v.String().Default("[DBFOLDER]"))
        .Variable("DB_LDFLOCATION", v => v.String().Default("[DBFOLDER]"))

        // Database connection properties (set by UI pages)
        .Variable("DB_SERVER", v => v.String().Default(""))
        .Variable("DB_DATABASE", v => v.String().Default(""))
        .Variable("DB_USER", v => v.String().Default(""))
        .Variable("DB_PASSWORD", v => v.String().Default("").Secret())
        .Variable("DB_INTEGRATEDSECURITY", v => v.String().Default(""))
        .Variable("DB_ATTACHINTEGRATEDSECURITY", v => v.String().Default(""))
        .Variable("DB_ATTACHUSER", v => v.String().Default(""))
        .Variable("DB_ATTACHPASSWORD", v => v.String().Default("").Secret())
        .Variable("SERVER_MDFLOCATION", v => v.String().Default(""))
        .Variable("SERVER_LDFLOCATION", v => v.String().Default(""))

        // Install folders (per-product, set by UI pages)
        .Variable("INSTALLFOLDERMA", v => v.String().Default(""))
        .Variable("INSTALLFOLDERMS", v => v.String().Default(""))
        .Variable("INSTALLFOLDERCO", v => v.String().Default(""))
        .Variable("INSTALLFOLDERKO", v => v.String().Default(""))
        .Variable("DBFOLDER", v => v.String().Default(""))
        .Variable("LOG_PATH", v => v.String().Default(""))

        // Service configuration
        .Variable("SERVICENAME", v => v.String().Default(""))
        .Variable("MSASSERVICE", v => v.String().Default(""))

        // DatabaseSetup additional paths
        .Variable("SENDFILEDIRECTORY", v => v.String().Default(""))
        .Variable("DBBACKUP", v => v.String().Default(""))

        // -----------------------------------------------------------------
        // Chain — mirrors WiX <Chain> element order
        // -----------------------------------------------------------------
        .Chain(chain =>
        {
            // =============================================================
            // Section 1: Prerequisites (PackageGroup "Checks")
            //
            // GAP: FalkForge does not support PackageGroupRef or external
            // prerequisite bootstrapper packages (NetFx472Redist, VCRedist,
            // OdbcDriver17, Sql2017). These would be separate MSI/EXE
            // packages with detection via registry search conditions.
            //
            // WiX equivalent:
            //   <PackageGroupRef Id="NetFx472Redist" />
            //   <PackageGroupRef Id="VCRedist" After="NetFx472Redist" />
            //   <PackageGroupRef Id="OdbcDriver17" After="VCRedist" />
            //   <PackageGroupRef Id="Sql2017" After="OdbcDriver17" />
            //
            // GAP: FalkForge does not support util:RegistrySearch for
            // detection variables (VCIsInstalled, ODBCIsInstalled).
            // =============================================================

            // Rollback boundary between prerequisites and product packages
            chain.RollbackBoundary("ProductBoundary");

            // =============================================================
            // Section 2: Product MSI packages
            // =============================================================

            // MultiAccess MSI — installed when MULTIACCESS = "true"
            // WiX: After="Sql2017" (after prerequisites)
            chain.MsiPackage(MsiPath("MultiAccess"), p => p
                .Id("MultiAccessMsi")
                .DisplayName("MultiAccess")
                .Version("8.9.0")
                .Vital(true)
                .InstallCondition("MULTIACCESS = \"true\"")
                .Property("INSTALLFOLDER", "[INSTALLFOLDERMA]")
                .Property("DBFOLDER", "[DBFOLDER]")
                .Property("INSTALLDB", "[INSTALLDB]")
                .Property("ATTACHDATABASE", "[ATTACHDATABASE]")
                .Property("DB_USER", "[DB_USER]")
                .Property("DB_PASSWORD", "[DB_PASSWORD]")
                .Property("DB_MDFLOCATION", "[DB_MDFLOCATION]")
                .Property("DB_LDFLOCATION", "[DB_LDFLOCATION]")
                .Property("DB_SERVER", "[DB_SERVER]")
                .Property("DB_DATABASE", "[DB_DATABASE]")
                .Property("DB_ATTACHINTEGRATEDSECURITY", "[DB_ATTACHINTEGRATEDSECURITY]")
                .Property("DB_ATTACHUSER", "[DB_ATTACHUSER]")
                .Property("DB_ATTACHPASSWORD", "[DB_ATTACHPASSWORD]")
                .Property("DB_INTEGRATEDSECURITY", "[DB_INTEGRATEDSECURITY]")
                .Property("SERVER_MDFLOCATION", "[SERVER_MDFLOCATION]")
                .Property("SERVER_LDFLOCATION", "[SERVER_LDFLOCATION]"));

            // MultiServer MSI — installed when MULTISERVER = "true"
            // WiX: After="MultiAccessMsi"
            chain.MsiPackage(MsiPath("MultiServer"), p => p
                .Id("MultiServerMsi")
                .DisplayName("MultiServer")
                .Version("8.9.0")
                .Vital(true)
                .InstallCondition("MULTISERVER = \"true\"")
                .Property("DB_SERVER", "[DB_SERVER]")
                .Property("DB_DATABASE", "[DB_DATABASE]")
                .Property("DB_USER", "[DB_USER]")
                .Property("DB_PASSWORD", "[DB_PASSWORD]")
                .Property("DB_INTEGRATEDSECURITY", "[DB_INTEGRATEDSECURITY]")
                .Property("LOG_PATH", "[LOG_PATH]")
                .Property("INSTALLFOLDER", "[INSTALLFOLDERMS]")
                .Property("ASSERVICE", "[MSASSERVICE]")
                .Property("SERVICENAME", "[SERVICENAME]")
                .Property("SERVICEACCOUNT", "[SERVICEACCOUNT]")
                .Property("SERVICEPASSWORD", "[SERVICEPASSWORD]")
                .Property("ODBCNAME", "[ODBCNAME]"));

            // Concatenate MSI — installed when CONCATENATE = "true"
            // WiX: After="MultiAccessMsi"
            chain.MsiPackage(MsiPath("Concatenate"), p => p
                .Id("ConcatenateMsi")
                .DisplayName("Concatenate")
                .Version("8.9.0")
                .Vital(true)
                .InstallCondition("CONCATENATE = \"true\"")
                .Property("INSTALLFOLDER", "[INSTALLFOLDERCO]"));

            // Konfigurera MSI — installed when KONFIGURERA = "true"
            // WiX: After="ConcatenateMsi"
            chain.MsiPackage(MsiPath("Konfigurera"), p => p
                .Id("KonfigureraMsi")
                .DisplayName("Konfigurera")
                .Version("8.9.0")
                .Vital(true)
                .InstallCondition("KONFIGURERA = \"true\"")
                .Property("INSTALLFOLDER", "[INSTALLFOLDERKO]"));

            // =============================================================
            // Section 3: Post-install EXE packages
            // =============================================================

            // DatabaseSetup EXE — runs when ATTACHDATABASE ~= "true" OR INSTALLDB ~= "true"
            // WiX: Permanent="yes" PerMachine="yes"
            // GAP: FalkForge does not support Permanent (package never uninstalled).
            //       The exe will be subject to normal uninstall handling.
            chain.ExePackage(stubExe, p => p
                .Id("DatabaseSetupExe")
                .DisplayName("Database Setup")
                .Vital(true)
                .InstallCondition("ATTACHDATABASE ~= \"true\" OR INSTALLDB ~= \"true\"")
                .Property("InstallArguments",
                    "/mode all"
                    + " /installfolder \"[INSTALLFOLDERMA]\""
                    + " /dbserver \"[DB_SERVER]\""
                    + " /dbname \"[DB_DATABASE]\""
                    + " /mdfpath \"[DB_MDFLOCATION]\""
                    + " /ldfpath \"[DB_LDFLOCATION]\""
                    + " /user \"[DB_USER]\""
                    + " /password \"[DB_PASSWORD]\""
                    + " /integratedsecurity \"[DB_INTEGRATEDSECURITY]\""
                    + " /attachdatabase \"[ATTACHDATABASE]\""
                    + " /sendfilefolder \"[SENDFILEDIRECTORY]\""
                    + " /backupfolder \"[DBBACKUP]\""
                    + " /dbfolder \"[DBFOLDER]\""
                    + " /servermdflocation \"[SERVER_MDFLOCATION]\""
                    + " /serverldflocation \"[SERVER_LDFLOCATION]\"")
                .ExitCode(0, ExitCodeBehavior.Success));

            // OdbcSetup EXE — runs when ASSERVICE ~= "true"
            // WiX: Permanent="yes" PerMachine="yes"
            // GAP: FalkForge does not support Permanent (package never uninstalled).
            chain.ExePackage(stubExe, p => p
                .Id("OdbcSetupExe")
                .DisplayName("ODBC Setup")
                .Vital(true)
                .InstallCondition("ASSERVICE ~= \"true\"")
                .Property("InstallArguments",
                    "/odbcname \"[ODBCNAME]\""
                    + " /dbserver \"[DB_SERVER]\""
                    + " /dbname \"[DB_DATABASE]\""
                    + " /dbuser \"[DB_USER]\""
                    + " /dbpassword \"[DB_PASSWORD]\""
                    + " /integratedsecurity \"[DB_INTEGRATEDSECURITY]\"")
                .ExitCode(0, ExitCodeBehavior.Success));
        })

        .Build();

    var compiler = new BundleCompiler();

    // Use pre-published NativeAOT engine binary as the bootstrapper stub
    var enginePath = Environment.GetEnvironmentVariable("FALKFORGE_ENGINE_PATH");
    if (enginePath is not null)
        compiler.EngineStubPath = enginePath;

    return compiler.Compile(bundle, outputPath);
});
