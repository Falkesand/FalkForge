using FalkForge;
using FalkForge.Builders;
using FalkForge.Compiler.Msi;
using FalkForge.Models;

// Define a custom MSI table with typed columns and data rows.
return Installer.Build(args, package =>
{
    package.Name = "Custom Tables Demo";
    package.Manufacturer = "Demo";
    package.Version = new Version(1, 0, 0);

    package.Files(files => files
        .Add("payload/app.exe")
        .To(KnownFolder.ProgramFiles / "Demo" / "CustomTableDemo"));

    package.CustomTable(ct => ct
        .Name("AppConfig")
        .Column("Key", CustomTableColumnType.String, col => col.PrimaryKey().Width(72))
        .Column("Value", CustomTableColumnType.String, col => col.Width(255))
        .Column("Priority", CustomTableColumnType.Int32)
        .Row(row => row.Set("Key", "Theme").Set("Value", "Dark").Set("Priority", 1))
        .Row(row => row.Set("Key", "Language").Set("Value", "en-US").Set("Priority", 2)));

}, new MsiCompiler());
