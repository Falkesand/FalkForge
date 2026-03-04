# Demo 31: SQL Server Extension

Creates a SQL Server database and executes a schema script during MSI installation. This demo defines a database on the local server instance, configures it for creation on install with overwrite confirmation, and sequences a SQL script to run after database creation.

## What This Demonstrates

- Creating a `SqlExtension` instance and defining a database with `DefineDatabase`
- Fluent builder for database configuration (server, name, create-on-install, overwrite behavior)
- Using `SqlScriptBuilder` to define SQL scripts bound to a database reference
- Sequencing scripts with `Sequence()` to control execution order
- `Result<T>` pattern for error handling on both database and script builders

## Key API Calls

```csharp
var sql = new SqlExtension();

var dbRef = sql.DefineDatabase(db => db
    .Id("AppDb")
    .Server(".")
    .Database("DemoDb")
    .CreateOnInstall()
    .ConfirmOverwrite());

var script = new SqlScriptBuilder()
    .Id("CreateSchema")
    .Database(dbRef.Value)
    .SourceFile("payload\\schema.sql")
    .ExecuteOnInstall()
    .Sequence(1)
    .Build();

sql.Scripts.Add(script.Value);
```

## How to Build

```shell
dotnet build demo/31-ext-sql/31-ext-sql.csproj
```

## Notes

- `Server(".")` targets the local default SQL Server instance. Change this to a named instance or remote server as needed.
- `ConfirmOverwrite()` prompts the user before overwriting an existing database.
- `DefineDatabase` returns a `Result<T>` containing a database reference that is passed to `SqlScriptBuilder.Database()` to link scripts to their target database.
- Scripts execute in the order defined by `Sequence()`. Use ascending integers to control ordering when multiple scripts are present.
- In production, extensions register automatically via the FalkForge SDK extension pipeline during compilation.
