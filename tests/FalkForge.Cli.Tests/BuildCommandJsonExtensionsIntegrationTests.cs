using System.Runtime.Versioning;
using FalkForge.Cli.Commands;
using FalkForge.Cli.Settings;
using FalkForge.Compiler.Msi;
using Spectre.Console.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

/// <summary>
/// Proves that a JSON <c>extensions</c> block (firewall / IIS / SQL) is genuinely translated into the
/// corresponding real extension and emitted into the compiled MSI by <c>forge build &lt;fixture&gt;.json</c>
/// — the SAME table output the C# fluent <c>new MsiCompiler().Use(extension)</c> path produces. These
/// tests fail while the JSON path hard-fails with JSN019 (the bug being fixed).
/// </summary>
[SupportedOSPlatform("windows")]
[Collection("SourceDateEpoch")]
public sealed class BuildCommandJsonExtensionsIntegrationTests
{
    private static CommandContext CreateContext() =>
        new([], new EmptyRemainingArguments(), "build", null);

    private static string BuildMsi(string tempDir, string json, params (string RelativePath, string Content)[] payload)
    {
        var payloadDir = Path.Combine(tempDir, "payload");
        Directory.CreateDirectory(payloadDir);
        foreach (var (relativePath, content) in payload)
            File.WriteAllText(Path.Combine(tempDir, relativePath), content);

        var jsonPath = Path.Combine(tempDir, "installer.json");
        File.WriteAllText(jsonPath, json);

        var outputDir = Path.Combine(tempDir, "output");
        Directory.CreateDirectory(outputDir);

        var console = new TestConsoleOutput();
        var command = new BuildCommand(console);
        var settings = new BuildSettings { ProjectPath = jsonPath, OutputPath = outputDir };

        var result = command.ExecuteSync(CreateContext(), settings, CancellationToken.None);
        Assert.True(result == ExitCodes.Success, $"build failed (exit {result}): {string.Join(" | ", console.AllOutput)}");

        var msiFiles = Directory.GetFiles(outputDir, "*.msi");
        Assert.NotEmpty(msiFiles);
        return msiFiles[0];
    }

    [Fact]
    public void Build_JsonFirewallExtension_EmitsWixFirewallExceptionRow()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var tempDir = Path.Combine(Path.GetTempPath(), $"ForgeJsonFw_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var json = """
            {
                "product": {
                    "name": "FwJsonApp",
                    "manufacturer": "TestCorp",
                    "version": "1.0.0",
                    "upgradeCode": "11111111-2222-3333-4444-555555555501"
                },
                "installDirectory": "TestCorp\\FwJsonApp",
                "features": [
                    { "id": "Main", "default": true, "files": [ { "source": "payload/app.exe" } ] }
                ],
                "extensions": {
                    "firewall": [
                        {
                            "id": "HttpRule",
                            "name": "Allow HTTP",
                            "protocol": "Tcp",
                            "port": "8080",
                            "direction": "Inbound",
                            "action": "Allow",
                            "profile": "All"
                        }
                    ]
                }
            }
            """;

            var msiPath = BuildMsi(tempDir, json, ("payload/app.exe", "payload"));

            using var db = MsiDatabase.Open(msiPath, readOnly: true).Value;
            var rows = db.QueryRows(
                "SELECT `Name`, `Port` FROM `WixFirewallException` WHERE `Name` = 'Allow HTTP'", fieldCount: 2);
            Assert.True(rows.IsSuccess, rows.IsFailure ? rows.Error.Message : null);
            Assert.Single(rows.Value);
            Assert.Equal("8080", rows.Value[0][1]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Build_JsonIisExtension_EmitsIisWebSiteRow()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var tempDir = Path.Combine(Path.GetTempPath(), $"ForgeJsonIis_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var json = """
            {
                "product": {
                    "name": "IisJsonApp",
                    "manufacturer": "TestCorp",
                    "version": "1.0.0",
                    "upgradeCode": "11111111-2222-3333-4444-555555555502"
                },
                "installDirectory": "TestCorp\\IisJsonApp",
                "features": [
                    { "id": "Main", "default": true, "files": [ { "source": "payload/webapp.dll" } ] }
                ],
                "extensions": {
                    "iis": {
                        "appPools": [
                            { "id": "DemoPool", "name": "DemoPool", "pipelineMode": "Integrated", "identity": "ApplicationPoolIdentity" }
                        ],
                        "webSites": [
                            {
                                "id": "DemoSite",
                                "description": "Demo Web Site",
                                "directory": "[INSTALLDIR]wwwroot",
                                "appPool": "DemoPool",
                                "bindings": [ { "protocol": "http", "port": 8080 } ]
                            }
                        ]
                    }
                }
            }
            """;

            var msiPath = BuildMsi(tempDir, json, ("payload/webapp.dll", "payload"));

            using var db = MsiDatabase.Open(msiPath, readOnly: true).Value;

            var siteRows = db.QueryRows(
                "SELECT `WebSite`, `Port` FROM `IIsWebSite` WHERE `WebSite` = 'DemoSite'", fieldCount: 2);
            Assert.True(siteRows.IsSuccess, siteRows.IsFailure ? siteRows.Error.Message : null);
            Assert.Single(siteRows.Value);
            Assert.Equal("8080", siteRows.Value[0][1]);

            var poolRows = db.QueryRows(
                "SELECT `AppPool` FROM `IIsAppPool` WHERE `AppPool` = 'DemoPool'", fieldCount: 1);
            Assert.True(poolRows.IsSuccess, poolRows.IsFailure ? poolRows.Error.Message : null);
            Assert.Single(poolRows.Value);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Build_JsonSqlExtension_EmitsSqlDatabaseAndScriptRows()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");

        var tempDir = Path.Combine(Path.GetTempPath(), $"ForgeJsonSql_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var json = """
            {
                "product": {
                    "name": "SqlJsonApp",
                    "manufacturer": "TestCorp",
                    "version": "1.0.0",
                    "upgradeCode": "11111111-2222-3333-4444-555555555503"
                },
                "installDirectory": "TestCorp\\SqlJsonApp",
                "features": [
                    {
                        "id": "Main",
                        "default": true,
                        "files": [ { "source": "payload/app.exe" }, { "source": "payload/schema.sql" } ]
                    }
                ],
                "extensions": {
                    "sql": [
                        {
                            "id": "AppDb",
                            "server": ".",
                            "database": "DemoDb",
                            "createOnInstall": true,
                            "scripts": [
                                { "id": "CreateSchema", "sourceFile": "payload/schema.sql", "executeOnInstall": true, "sequence": 1 }
                            ]
                        }
                    ]
                }
            }
            """;

            var msiPath = BuildMsi(tempDir, json,
                ("payload/app.exe", "payload"),
                ("payload/schema.sql", "CREATE TABLE T (Id INT);"));

            using var db = MsiDatabase.Open(msiPath, readOnly: true).Value;

            var dbRows = db.QueryRows(
                "SELECT `Id`, `Server`, `Database` FROM `SqlDatabase` WHERE `Id` = 'AppDb'", fieldCount: 3);
            Assert.True(dbRows.IsSuccess, dbRows.IsFailure ? dbRows.Error.Message : null);
            Assert.Single(dbRows.Value);
            Assert.Equal(".", dbRows.Value[0][1]);
            Assert.Equal("DemoDb", dbRows.Value[0][2]);

            var scriptRows = db.QueryRows(
                "SELECT `Id` FROM `SqlScript` WHERE `Id` = 'CreateSchema'", fieldCount: 1);
            Assert.True(scriptRows.IsSuccess, scriptRows.IsFailure ? scriptRows.Error.Message : null);
            Assert.Single(scriptRows.Value);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
