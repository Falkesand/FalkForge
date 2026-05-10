using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Extensions.Firewall;
using FalkForge.Extensions.Iis;
using FalkForge.Extensions.Iis.Models;
using FalkForge.Extensions.Sql;
using FalkForge.Extensions.Sql.Builders;
using FalkForge.Extensions.DotNet;
using FalkForge.Extensions.Util;
using FalkForge.Extensions.Util.XmlConfig;
using FalkForge.Extensions.Util.QuietExec;

// ============================================================================
// Extensions Showcase
// ============================================================================
// Demonstrates all FalkForge extension APIs in a single project:
//   - Firewall: Inbound/outbound TCP rules
//   - IIS:      Application pool + web site with HTTP binding
//   - SQL:      Database creation + schema script execution
//   - .NET:     Runtime detection (.NET 8.0+ x64)
//   - Util:     XmlConfig transformation + QuietExec post-install command
//
// Each extension is configured independently. In production, the FalkForge SDK
// wires extension registration automatically during compilation.
// ============================================================================

// -- Firewall Extension -------------------------------------------------------
// Opens TCP port 8080 inbound for the web application on all network profiles,
// and allows outbound SQL traffic for database connectivity.

var firewall = new FirewallExtension();

firewall.AddRule(rule => rule
    .Id("AllowHttp8080")
    .Name("WebApp HTTP Inbound")
    .Description("Allow inbound HTTP traffic on port 8080 for ExtensionsShowcase")
    .Protocol(FirewallProtocol.Tcp)
    .Port("8080")
    .Direction(FirewallDirection.Inbound)
    .Action(FirewallRuleAction.Allow)
    .Profile(FirewallProfile.All));

firewall.AddRule(rule => rule
    .Id("AllowSqlOutbound")
    .Name("WebApp SQL Outbound")
    .Description("Allow outbound SQL Server traffic on port 1433")
    .Protocol(FirewallProtocol.Tcp)
    .Port("1433")
    .Direction(FirewallDirection.Outbound)
    .Action(FirewallRuleAction.Allow)
    .Profile(FirewallProfile.Domain | FirewallProfile.Private));

Console.WriteLine($"[Firewall] {firewall.Name} extension: 2 rules configured. Validation runs automatically during compilation.");

// -- IIS Extension ------------------------------------------------------------
// Creates an application pool running no managed code (for .NET Core) and
// a web site bound to HTTP port 8080.

var iis = new IisExtension();

var appPool = iis.DefineAppPool(pool => pool
    .Id("ShowcaseAppPool")
    .Name("ShowcaseAppPool")
    .NoManagedCode()
    .PipelineMode(ManagedPipelineMode.Integrated)
    .Identity(AppPoolIdentityType.ApplicationPoolIdentity)
    .IdleTimeout(30)
    .RecycleMinutes(1740));

iis.AddWebSite(site => site
    .Id("ShowcaseWebSite")
    .Description("Extensions Showcase Web Site")
    .Directory("[INSTALLDIR]wwwroot")
    .AppPool(appPool)
    .Binding(8080, "http")
    .AutoStart(true)
    .ConnectionTimeout(120));

Console.WriteLine($"[IIS] {iis.Name} extension: {iis.AppPools.Count} pool(s), {iis.WebSites.Count} site(s). Validation runs automatically during compilation.");

// -- SQL Extension ------------------------------------------------------------
// Defines a database and a SQL script that creates the initial schema.
// The script runs on install only; the database is not dropped on uninstall.

var sql = new SqlExtension();

var dbRef = sql.DefineDatabase(db => db
    .Id("AppDb")
    .Server("[SQLSERVER]")
    .Database("ExtShowcaseDb")
    .CreateOnInstall()
    .ConfirmOverwrite());

if (dbRef.IsFailure)
{
    Console.Error.WriteLine($"SQL: {dbRef.Error}");
    return 1;
}

var scriptResult = new SqlScriptBuilder()
    .Id("CreateTables")
    .Database(dbRef.Value)
    .SourceFile("payload\\create-tables.sql")
    .ExecuteOnInstall()
    .Sequence(1)
    .Build();

if (scriptResult.IsFailure)
{
    Console.Error.WriteLine($"SQL: {scriptResult.Error}");
    return 1;
}

sql.Scripts.Add(scriptResult.Value);

Console.WriteLine($"[SQL] {sql.Name} extension: 1 database, 1 script.");

// -- .NET Detection Extension -------------------------------------------------
// Searches for .NET 8.0+ Runtime on x64. The detected version is stored in a
// variable that can be referenced in install conditions.

var dotnet = new DotNetExtension();

var searchResult = new DotNetCoreSearchBuilder()
    .RuntimeType(DotNetRuntimeType.Runtime)
    .Platform(DotNetPlatform.X64)
    .MinVersion(new Version(8, 0, 0))
    .Variable("DOTNET8_FOUND")
    .Build();

if (searchResult.IsFailure)
{
    Console.Error.WriteLine($".NET Detection: {searchResult.Error}");
    return 1;
}

var dotnetSearch = searchResult.Value;
Console.WriteLine(
    $"[DotNet] {dotnet.Name} extension: search {dotnetSearch.RuntimeType} >= "
    + $"{dotnetSearch.MinimumVersion} ({dotnetSearch.Platform}) -> ${dotnetSearch.VariableName}.");

// -- Util Extension: XmlConfig ------------------------------------------------
// Transforms web.config at install time to inject the correct SQL connection
// string and environment name from MSI properties.

var util = new UtilExtension();

var connStringConfig = new XmlConfigBuilder()
    .Id("SetConnectionString")
    .File("[INSTALLDIR]web.config")
    .XPath("//connectionStrings/add[@name='AppDb']")
    .SetAttribute("connectionString", "Server=[SQLSERVER];Database=ExtShowcaseDb;Trusted_Connection=True;")
    .Sequence(1)
    .Build();

if (connStringConfig.IsFailure)
{
    Console.Error.WriteLine($"XmlConfig: {connStringConfig.Error}");
    return 1;
}

util.XmlConfig.Add(connStringConfig.Value);

var envConfig = new XmlConfigBuilder()
    .Id("SetEnvironment")
    .File("[INSTALLDIR]web.config")
    .XPath("//appSettings/add[@key='Environment']")
    .SetAttribute("value", "[ENVIRONMENT]")
    .Sequence(2)
    .Build();

if (envConfig.IsFailure)
{
    Console.Error.WriteLine($"XmlConfig: {envConfig.Error}");
    return 1;
}

util.XmlConfig.Add(envConfig.Value);

Console.WriteLine($"[Util] {util.Name} extension: 2 XmlConfig entries.");

// -- Util Extension: QuietExec ------------------------------------------------
// Defines a silent post-install command. The QuietExecModel describes the
// command; at install time the engine executes it with no visible console window.

var quietExec = new QuietExecModel
{
    Id = "InitializeApp",
    CommandLine = "\"[INSTALLDIR]webapp.dll\" --init --environment [ENVIRONMENT]",
    WorkingDirectory = "[INSTALLDIR]",
};

Console.WriteLine($"[Util] QuietExec: command '{quietExec.Id}' configured.");

// -- Package Definition -------------------------------------------------------
// Assembles the MSI package with all application files. In production the
// extensions above are registered via the SDK's extension pipeline, emitting
// their custom MSI tables during compilation.

return Installer.Build(args, package =>
{
    package.Name = "ExtensionsShowcase";
    package.Manufacturer = "Contoso";
    package.Version = new Version(1, 0, 0);
    package.Description = "Demonstrates all FalkForge extensions: Firewall, IIS, SQL, .NET, Util";
    package.Scope = InstallScope.PerMachine;
    package.Architecture = ProcessorArchitecture.X64;

    package.UseDialogSet(MsiDialogSet.InstallDir);

    package.MajorUpgrade(_ => { });
    package.Downgrade(d => d.Block("A newer version of Extensions Showcase is already installed."));

    package.Feature("Complete", f =>
    {
        f.Title = "Extensions Showcase";
        f.Description = "Web application with database, IIS, firewall, and .NET runtime support";
        f.IsRequired = true;

        f.Files(fs => fs
            .Add("payload/webapp.dll")
            .Add("payload/web.config")
            .Add("payload/create-tables.sql")
            .To(KnownFolder.ProgramFiles / "Contoso" / "ExtensionsShowcase"));
    });

}, new MsiCompiler(new WindowsFileSystem()));
