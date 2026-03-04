# Demo 26: Custom Tables

Defines a custom table inside the MSI database with typed columns and data rows. Useful for storing application
configuration or metadata that custom actions can read at install time.

## What This Demonstrates

- Creating a custom MSI table with `package.CustomTable()`
- Defining typed columns (string, int) with constraints (primary key, width)
- Inserting data rows into the custom table

## Key API Calls

```csharp
package.CustomTable(ct => ct
    .Name("AppConfig")
    .Column("Key", CustomTableColumnType.String, col => col.PrimaryKey().Width(72))
    .Column("Value", CustomTableColumnType.String, col => col.Width(255))
    .Column("Priority", CustomTableColumnType.Int32)
    .Row(row => row.Set("Key", "Theme").Set("Value", "Dark").Set("Priority", 1))
    .Row(row => row.Set("Key", "Language").Set("Value", "en-US").Set("Priority", 2)));
```

## How to Build

```bash
dotnet build demo/26-custom-tables
```

## Notes

- Custom tables are embedded in the MSI database and can be queried by custom actions using the MSI API (
  `MsiOpenDatabase`, `MsiDatabaseOpenView`).
- `PrimaryKey()` marks a column as part of the table's primary key. At least one column should be a primary key.
- `Width()` sets the maximum character length for string columns. This maps to the MSI column definition width.
