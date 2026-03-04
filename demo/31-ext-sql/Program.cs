using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Models;
using FalkForge.Extensions.Sql;
using FalkForge.Extensions.Sql.Builders;

// Create a SQL Server database and run a schema script.
var sql = new SqlExtension();

var dbRef = sql.DefineDatabase(db => db
    .Id("AppDb")
    .Server(".")
    .Database("DemoDb")
    .CreateOnInstall()
    .ConfirmOverwrite());

if (dbRef.IsFailure)
{
    Console.Error.WriteLine(dbRef.Error);
    return 1;
}

var script = new SqlScriptBuilder()
    .Id("CreateSchema")
    .Database(dbRef.Value)
    .SourceFile("payload\\schema.sql")
    .ExecuteOnInstall()
    .Sequence(1)
    .Build();

if (script.IsFailure)
{
    Console.Error.WriteLine(script.Error);
    return 1;
}

sql.Scripts.Add(script.Value);
Console.WriteLine($"SQL: 1 database, 1 script configured.");

return Installer.Build(args, package =>
{
    package.Name = "SQL Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.Files(files => files
        .Add("payload/app.exe")
        .Add("payload/schema.sql")
        .To(KnownFolder.ProgramFiles / "Demo" / "SqlDemo"));

}, new MsiCompiler());
