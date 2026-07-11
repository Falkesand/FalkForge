using System.Globalization;
using System.Runtime.Versioning;
using FalkForge.Models;
using FalkForge.Platform.Windows;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests;

[SupportedOSPlatform("windows")]
public sealed class MsiCompilerIntegrationTests
{
    [Fact]
    public void Compile_ValidPackageWithFiles_CreatesMsiFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceDir = Path.Combine(tempDir, "source");
            Directory.CreateDirectory(sourceDir);
            var sourceFile = Path.Combine(sourceDir, "app.exe");
            File.WriteAllText(sourceFile, "fake executable content for testing");

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "IntegrationTestApp";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(1, 0, 0);
                p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "IntegrationTestApp"));
            });

            var fileSystem = new WindowsFileSystem();
            var compiler = new MsiCompiler(fileSystem);

            var result = compiler.Compile(package, outputDir);

            Assert.True(result.IsSuccess, $"Compile failed: {(result.IsFailure ? result.Error.Message : "")}");
            Assert.True(File.Exists(result.Value), $"MSI file not found at: {result.Value}");
            Assert.True(new FileInfo(result.Value).Length > 0, "MSI file is empty");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Compile_ValidPackageWithFiles_MsiHasExpectedTables()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceDir = Path.Combine(tempDir, "source");
            Directory.CreateDirectory(sourceDir);
            var sourceFile = Path.Combine(sourceDir, "app.exe");
            File.WriteAllText(sourceFile, "fake executable content for table test");

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "TableTestApp";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(2, 0, 0);
                p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "TableTestApp"));
            });

            var fileSystem = new WindowsFileSystem();
            var compiler = new MsiCompiler(fileSystem);

            var compileResult = compiler.Compile(package, outputDir);
            Assert.True(compileResult.IsSuccess, $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

            // Open the MSI and verify expected tables exist by querying them
            var dbResult = MsiDatabase.Open(compileResult.Value, readOnly: true);
            Assert.True(dbResult.IsSuccess, $"Failed to open MSI: {(dbResult.IsFailure ? dbResult.Error.Message : "")}");

            using var db = dbResult.Value;

            // These core tables should exist in any valid MSI with files
            AssertTableExists(db, "Property");
            AssertTableExists(db, "File");
            AssertTableExists(db, "Component");
            AssertTableExists(db, "Directory");
            AssertTableExists(db, "Feature");
            AssertTableExists(db, "FeatureComponents");
            AssertTableExists(db, "Media");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Compile_InvalidPackage_ReturnsValidationError()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            // Build a package with invalid data (empty name/manufacturer)
            var package = new PackageModel
            {
                Name = "",
                Manufacturer = "",
                Version = new Version(1, 0, 0),
                UpgradeCode = Guid.NewGuid(),
                ProductCode = Guid.NewGuid(),
                Features =
                [
                    new FeatureModel
                    {
                        Id = "Complete",
                        Title = "Complete",
                        IsRequired = true,
                        IsDefault = true
                    }
                ]
            };

            var fileSystem = new WindowsFileSystem();
            var compiler = new MsiCompiler(fileSystem);

            var result = compiler.Compile(package, tempDir);

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorKind.Validation, result.Error.Kind);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Compile_FromDirectoryWithSubdirectory_EmitsDirectoryRowsForEveryComponentDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            // Source layout (depth-2 guard — bug reproduces at any depth >= 1):
            //   source/
            //     Service/
            //       PlugWarden.Service.exe
            //     Service/Plugins/
            //       plugin.dll
            var sourceDir = Path.Combine(tempDir, "source");
            var serviceSubdir = Path.Combine(sourceDir, "Service");
            var pluginsSubdir = Path.Combine(serviceSubdir, "Plugins");
            Directory.CreateDirectory(pluginsSubdir);
            File.WriteAllText(Path.Combine(serviceSubdir, "PlugWarden.Service.exe"), "fake service content");
            File.WriteAllText(Path.Combine(pluginsSubdir, "plugin.dll"), "fake plugin content");

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var installDir = KnownFolder.ProgramFiles / "TestCorp" / "HarvestApp";
            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "HarvestApp";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(1, 0, 0);
                p.DefaultInstallDirectory = installDir;
                p.Files(f => f.FromDirectory(sourceDir).To(installDir));
            });

            var fileSystem = new WindowsFileSystem();
            var compiler = new MsiCompiler(fileSystem);

            var compileResult = compiler.Compile(package, outputDir);
            Assert.True(compileResult.IsSuccess, $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

            var dbResult = MsiDatabase.Open(compileResult.Value, readOnly: true);
            Assert.True(dbResult.IsSuccess, $"Failed to open MSI: {(dbResult.IsFailure ? dbResult.Error.Message : "")}");

            using var db = dbResult.Value;

            var dirRowsResult = db.QueryRows("SELECT `Directory` FROM `Directory`", 1);
            Assert.True(dirRowsResult.IsSuccess);
            var directoryKeys = dirRowsResult.Value.Select(r => r[0]!).ToHashSet(StringComparer.Ordinal);

            var componentRowsResult = db.QueryRows("SELECT `Component`, `Directory_` FROM `Component`", 2);
            Assert.True(componentRowsResult.IsSuccess);

            foreach (var row in componentRowsResult.Value)
            {
                var componentId = row[0]!;
                var directoryRef = row[1]!;
                Assert.True(directoryKeys.Contains(directoryRef),
                    $"Component '{componentId}' references Directory '{directoryRef}' but no such row exists in the Directory table (triggers MSI error 2727).");
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Compile_PackageWithoutPermissions_DoesNotEmitLockPermissionTables()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceFile = Path.Combine(tempDir, "app.exe");
            File.WriteAllText(sourceFile, "fake content");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "NoPermsApp";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(1, 0, 0);
                p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "NoPermsApp"));
            });

            var fileSystem = new WindowsFileSystem();
            var compiler = new MsiCompiler(fileSystem);

            var compileResult = compiler.Compile(package, outputDir);
            Assert.True(compileResult.IsSuccess, $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

            var dbResult = MsiDatabase.Open(compileResult.Value, readOnly: true);
            Assert.True(dbResult.IsSuccess);
            using var db = dbResult.Value;

            // MSI validation error 1941: LockPermissions and MsiLockPermissionsEx must not both exist.
            // When the package has no Permission entries neither table should be materialized.
            Assert.False(TableExists(db, "LockPermissions"),
                "LockPermissions table emitted for a package with no permissions.");
            Assert.False(TableExists(db, "MsiLockPermissionsEx"),
                "MsiLockPermissionsEx table emitted for a package with no permissions.");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Compile_ShortcutOnStartMenuSubfolder_EmitsSubfolderAndParentDirectoryRows()
    {
        // MSI error 2756: "property 'SM_PlugWarden' used as directory but never assigned"
        // OnStartMenu("name") and OnStartup() previously referenced Directory IDs
        // (SM_<subfolder>, ProgramMenuFolder, StartupFolder, DesktopFolder) that
        // were never emitted into the Directory table.
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceFile = Path.Combine(tempDir, "app.exe");
            File.WriteAllText(sourceFile, "fake");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "ShortcutApp";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(1, 0, 0);
                p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "ShortcutApp"));
                p.Shortcut("App", "app.exe").OnStartMenu("MyAppFolder");
                p.Shortcut("App Autostart", "app.exe").OnStartup();
                p.Shortcut("App Desktop", "app.exe").OnDesktop();
            });

            var fileSystem = new WindowsFileSystem();
            var compileResult = new MsiCompiler(fileSystem).Compile(package, outputDir);
            Assert.True(compileResult.IsSuccess, $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

            using var db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;

            var dirRows = db.QueryRows("SELECT `Directory` FROM `Directory`", 1).Value
                .Select(r => r[0]!)
                .ToHashSet(StringComparer.Ordinal);

            var shortcutDirRefs = db.QueryRows("SELECT `Shortcut`, `Directory_` FROM `Shortcut`", 2).Value;
            foreach (var row in shortcutDirRefs)
            {
                var shortcutId = row[0]!;
                var directoryRef = row[1]!;
                Assert.True(dirRows.Contains(directoryRef),
                    $"Shortcut '{shortcutId}' references Directory '{directoryRef}' but no such row exists (MSI error 2756).");
            }

            Assert.Contains("ProgramMenuFolder", dirRows);
            Assert.Contains("StartupFolder", dirRows);
            Assert.Contains("DesktopFolder", dirRows);
            Assert.Contains(dirRows, id => id.StartsWith("SM_MyAppFolder_", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Compile_MediaTemplate_CabinetStreamNameMatchesMediaRow()
    {
        // MSI error 2356: "Couldn't locate cabinet in stream: data1.cab".
        // CabinetBuilder embedded the cab under the literal "Data.cab" while
        // EmitMediaFromTemplate wrote "data1.cab" (or whatever the template
        // produced) into Media.Cabinet — the installer looked up the stream
        // by the Media row name and found nothing.
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceFile = Path.Combine(tempDir, "app.exe");
            File.WriteAllText(sourceFile, "fake content for cab stream test");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "CabNameApp";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(1, 0, 0);
                p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "CabNameApp"));
                p.MediaTemplate(mt => mt
                    .CabinetTemplate("data{0}.cab")
                    .EmbedCabinet(true));
            });

            var fileSystem = new WindowsFileSystem();
            var compileResult = new MsiCompiler(fileSystem).Compile(package, outputDir);
            Assert.True(compileResult.IsSuccess);

            using var db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;

            var mediaCabs = db.QueryRows("SELECT `Cabinet` FROM `Media`", 1).Value
                .Select(r => r[0]!)
                .Select(name => name.StartsWith('#') ? name[1..] : name)
                .ToList();
            var streamNames = db.QueryRows("SELECT `Name` FROM `_Streams`", 1).Value
                .Select(r => r[0]!)
                .ToHashSet(StringComparer.Ordinal);

            // Pin the literal template expansion so a silent refactor of the format
            // evaluation cannot slip the contract while both sides still agree.
            Assert.Equal("data1.cab", Assert.Single(mediaCabs));
            Assert.Contains("data1.cab", streamNames);

            foreach (var cab in mediaCabs)
                Assert.True(streamNames.Contains(cab),
                    $"Media row references cabinet '{cab}' but no matching _Streams entry exists. Streams: [{string.Join(", ", streamNames)}]");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Compile_ServiceExecutableWithFullMsiPath_ResolvesComponentByFileName()
    {
        // Bug: EmitServices keyed the filename->component dictionary by FileName
        // but looked up service.Executable which holds a full MSI path (e.g.
        // "[INSTALLFOLDER]Service\PlugWarden.Service.exe"). The miss fell back
        // to the first component in the package, so the service's ImagePath in
        // the registry pointed at an unrelated file (in PlugWarden's case the
        // native sqlite helper), preventing the service from ever starting.
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceDir = Path.Combine(tempDir, "source");
            Directory.CreateDirectory(sourceDir);
            // Two files — a "sqlite helper" and the real service exe. FromDirectory
            // harvests in filesystem enumeration order (alphabetical on NTFS), so
            // 'e_sqlite3.dll' sorts ahead of 'PlugWarden.Service.exe' and becomes
            // the first component = defaultComponentId. Under the bug the service
            // fell through to that first component, pointing ImagePath at the
            // wrong file.
            File.WriteAllText(Path.Combine(sourceDir, "e_sqlite3.dll"), "native helper");
            File.WriteAllText(Path.Combine(sourceDir, "PlugWarden.Service.exe"), "service executable");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var installDir = KnownFolder.ProgramFiles / "TestCorp" / "SvcApp" / "Service";
            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "SvcApp";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(1, 0, 0);
                p.DefaultInstallDirectory = installDir;
                p.Files(f => f.FromDirectory(sourceDir).To(installDir));
                p.Service("MyService", svc =>
                {
                    svc.DisplayName = "My Service";
                    svc.Executable = "[INSTALLFOLDER]Service\\PlugWarden.Service.exe";
                    svc.StartMode = ServiceStartMode.Automatic;
                    svc.Account = ServiceAccount.LocalSystem;
                });
            });

            var fileSystem = new WindowsFileSystem();
            var compileResult = new MsiCompiler(fileSystem).Compile(package, outputDir);
            Assert.True(compileResult.IsSuccess, $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

            using var db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;

            var serviceComponentId = db.QueryRows(
                    "SELECT `Component_` FROM `ServiceInstall`", 1).Value
                .Select(r => r[0]!)
                .Single();

            var componentFileName = db.QueryRows(
                    "SELECT `Component_`, `FileName` FROM `File`", 2).Value
                .Single(r => string.Equals(r[0], serviceComponentId, StringComparison.Ordinal))[1]!;

            // Strip MSI short|long encoding to get the installed filename.
            var longName = componentFileName.Split('|').Last();

            Assert.Equal("PlugWarden.Service.exe", longName);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Compile_MediaTemplate_PayloadExceedingMaxCabSize_EmitsMultipleCabinetsWithMatchingStreams()
    {
        // Previously MsiCompiler always embedded a single disk-1 cabinet regardless
        // of how many Media rows TableEmitter emitted. Any payload large enough to
        // trigger the template's MaximumCabinetSizeInMB split produced an MSI whose
        // data2.cab, data3.cab, ... rows pointed at streams that did not exist.
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            // Three ~500KB files with a 1MB cap → two files fit in disk 1, the
            // third spills to disk 2.
            var payload = new byte[500 * 1024];
            new Random(12345).NextBytes(payload);
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var targetDir = KnownFolder.ProgramFiles / "TestCorp" / "MultiCab";
            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "MultiCabApp";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(1, 0, 0);
                p.DefaultInstallDirectory = targetDir;
                p.MediaTemplate(mt => mt
                    .CabinetTemplate("data{0}.cab")
                    .MaxCabinetSizeMB(1)
                    .EmbedCabinet(true));

                for (var i = 0; i < 3; i++)
                {
                    var path = Path.Combine(tempDir, $"payload{i}.bin");
                    File.WriteAllBytes(path, payload);
                    p.Files(f => f.Add(path).To(targetDir));
                }
            });

            var fileSystem = new WindowsFileSystem();
            var compileResult = new MsiCompiler(fileSystem).Compile(package, outputDir);
            Assert.True(compileResult.IsSuccess, $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

            using var db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;

            var mediaCabs = db.QueryRows("SELECT `Cabinet` FROM `Media`", 1).Value
                .Select(r => r[0]!)
                .Select(n => n.StartsWith('#') ? n[1..] : n)
                .ToList();
            var streamNames = db.QueryRows("SELECT `Name` FROM `_Streams`", 1).Value
                .Select(r => r[0]!)
                .ToHashSet(StringComparer.Ordinal);

            // Planner must have split the payload into at least two cabinets.
            Assert.True(mediaCabs.Count >= 2,
                $"Expected multi-cab split but only got: [{string.Join(", ", mediaCabs)}]");
            Assert.Contains("data1.cab", mediaCabs);
            Assert.Contains("data2.cab", mediaCabs);

            // Every Media row must have a matching embedded stream.
            foreach (var cab in mediaCabs)
                Assert.True(streamNames.Contains(cab),
                    $"Media row references cabinet '{cab}' but no matching _Streams entry exists. Streams: [{string.Join(", ", streamNames)}]");

            // Every File.Sequence must fall inside exactly one Media row's
            // [previousLastSequence+1 .. LastSequence] range. Guards against an
            // off-by-one between the planner's exclusive FileEndIndex and MSI's
            // inclusive LastSequence.
            var mediaBounds = db.QueryRows("SELECT `DiskId`, `LastSequence` FROM `Media`", 2).Value
                .Select(r => (DiskId: int.Parse(r[0]!, CultureInfo.InvariantCulture),
                              LastSequence: int.Parse(r[1]!, CultureInfo.InvariantCulture)))
                .OrderBy(x => x.DiskId)
                .ToList();
            var fileSequences = db.QueryRows("SELECT `Sequence` FROM `File`", 1).Value
                .Select(r => int.Parse(r[0]!, CultureInfo.InvariantCulture))
                .ToList();
            foreach (var seq in fileSequences)
            {
                var matches = mediaBounds.Count(b => seq <= b.LastSequence);
                Assert.True(matches >= 1,
                    $"File.Sequence {seq} falls outside every Media row's LastSequence bound [{string.Join(", ", mediaBounds.Select(b => b.LastSequence))}]");
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Compile_ShortcutsWithSubfoldersCollidingAfterSanitize_GetDistinctDirectoryRows()
    {
        // "My App" and "My-App" both sanitize to "My_App". Without hash disambiguation
        // one subfolder's Directory row would silently claim both shortcuts.
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceFile = Path.Combine(tempDir, "app.exe");
            File.WriteAllText(sourceFile, "fake");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "SubfolderCollisionApp";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(1, 0, 0);
                p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "SubCollApp"));
                p.Shortcut("First", "app.exe").OnStartMenu("My App");
                p.Shortcut("Second", "app.exe").OnStartMenu("My-App");
            });

            var fileSystem = new WindowsFileSystem();
            var compileResult = new MsiCompiler(fileSystem).Compile(package, outputDir);
            Assert.True(compileResult.IsSuccess);

            using var db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;

            var shortcutRefs = db.QueryRows("SELECT `Directory_` FROM `Shortcut`", 1).Value
                .Select(r => r[0]!)
                .Distinct()
                .ToList();

            Assert.Equal(2, shortcutRefs.Count);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Compile_PackageWithSddlPermission_EmitsOnlyMsiLockPermissionsExTable()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceFile = Path.Combine(tempDir, "app.exe");
            File.WriteAllText(sourceFile, "fake");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "SddlApp";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(1, 0, 0);
                p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "SddlApp"));
                p.Permission("SddlApp", perm => { perm.ForTable("File"); perm.Sddl = "D:(A;;RPWP;;;WD)"; });
            });

            var fileSystem = new WindowsFileSystem();
            var compileResult = new MsiCompiler(fileSystem).Compile(package, outputDir);
            Assert.True(compileResult.IsSuccess);

            using var db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;
            Assert.True(TableExists(db, "MsiLockPermissionsEx"));
            Assert.False(TableExists(db, "LockPermissions"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Compile_PackageWithUserPermission_EmitsOnlyLockPermissionsTable()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceFile = Path.Combine(tempDir, "app.exe");
            File.WriteAllText(sourceFile, "fake");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "UserApp";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(1, 0, 0);
                p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "UserApp"));
                p.Permission("UserApp", perm => { perm.ForTable("File"); perm.User = "Everyone"; perm.Permission = 0x10000000; });
            });

            var fileSystem = new WindowsFileSystem();
            var compileResult = new MsiCompiler(fileSystem).Compile(package, outputDir);
            Assert.True(compileResult.IsSuccess);

            using var db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;
            Assert.True(TableExists(db, "LockPermissions"));
            Assert.False(TableExists(db, "MsiLockPermissionsEx"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Compile_ServiceWithFailureActions_EmitsMsiServiceConfigFailureActionsRow()
    {
        // C3: ServiceBuilder.FailureActions(...) collects recovery/restart
        // configuration onto ServiceModel.FailureActions, but nothing in
        // FalkForge.Compiler.Msi emitted an MsiServiceConfigFailureActions row —
        // service recovery (restart-on-crash) was silently lost.
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceFile = Path.Combine(tempDir, "svc.exe");
            File.WriteAllText(sourceFile, "fake service executable");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "FailureActionsApp";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(1, 0, 0);
                p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "FailureActionsApp"));
                p.Service("MyService", svc =>
                {
                    svc.DisplayName = "My Service";
                    svc.Executable = "svc.exe";
                    svc.FailureActions(fa =>
                    {
                        fa.OnFirstFailure = FailureAction.Restart;
                        fa.OnSecondFailure = FailureAction.Restart;
                        fa.OnSubsequentFailures = FailureAction.RunCommand;
                        fa.ResetPeriod = TimeSpan.FromDays(2);
                        fa.RestartDelay = TimeSpan.FromSeconds(45);
                        fa.Command = "cmd.exe /c echo hi";
                    });
                });
            });

            var fileSystem = new WindowsFileSystem();
            var compileResult = new MsiCompiler(fileSystem).Compile(package, outputDir);
            Assert.True(compileResult.IsSuccess, $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

            using var db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;
            Assert.True(TableExists(db, "MsiServiceConfigFailureActions"));

            var serviceComponentId = db.QueryRows("SELECT `Component_` FROM `ServiceInstall`", 1).Value
                .Select(r => r[0]!)
                .Single();

            var row = db.QueryRows(
                    "SELECT `Name`, `Event`, `ResetPeriod`, `RebootMessage`, `Command`, `Actions`, `DelayActions`, `Component_` FROM `MsiServiceConfigFailureActions`",
                    8).Value
                .Single();

            Assert.Equal("MyService", row[0]);
            Assert.Equal(5, int.Parse(row[1]!, CultureInfo.InvariantCulture)); // install(1) | reinstall(4)
            Assert.Equal(172800, int.Parse(row[2]!, CultureInfo.InvariantCulture)); // 2 days in seconds
            Assert.Null(row[3]); // no RebootMessage configured
            Assert.Equal("cmd.exe /c echo hi", row[4]);
            Assert.Equal("1[~]1[~]3", row[5]); // Restart, Restart, RunCommand
            Assert.Equal("45000[~]45000[~]45000", row[6]); // all tiers share the 45s RestartDelay
            Assert.Equal(serviceComponentId, row[7]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Compile_ServiceWithoutFailureActions_DoesNotEmitMsiServiceConfigFailureActionsTable()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceFile = Path.Combine(tempDir, "svc.exe");
            File.WriteAllText(sourceFile, "fake service executable");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "NoFailureActionsApp";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(1, 0, 0);
                p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "NoFailureActionsApp"));
                p.Service("MyService", svc =>
                {
                    svc.DisplayName = "My Service";
                    svc.Executable = "svc.exe";
                });
            });

            var fileSystem = new WindowsFileSystem();
            var compileResult = new MsiCompiler(fileSystem).Compile(package, outputDir);
            Assert.True(compileResult.IsSuccess, $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

            using var db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;
            Assert.False(TableExists(db, "MsiServiceConfigFailureActions"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Compile_ServiceWithUserPermission_EmitsLockPermissionsRowKeyedByServiceInstallId()
    {
        // C4: ServiceBuilder.Permission(...) collects entries onto
        // ServiceModel.Permissions, but only the top-level PackageModel.Permissions
        // collection fed the LockPermissions producer — a permission set on a
        // service never reached the MSI. The LockObject must be the service's
        // ServiceInstall primary key ("SVC_" + sanitized name), not the raw
        // service name ServiceBuilder stamps onto the in-memory model.
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceFile = Path.Combine(tempDir, "svc.exe");
            File.WriteAllText(sourceFile, "fake service executable");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "SvcPermApp";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(1, 0, 0);
                p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "SvcPermApp"));
                p.Service("MyService", svc =>
                {
                    svc.DisplayName = "My Service";
                    svc.Executable = "svc.exe";
                    svc.Permission(perm =>
                    {
                        perm.Domain = "BUILTIN";
                        perm.User = "Administrators";
                        perm.Permission = 0x000F01FF;
                    });
                });
            });

            var fileSystem = new WindowsFileSystem();
            var compileResult = new MsiCompiler(fileSystem).Compile(package, outputDir);
            Assert.True(compileResult.IsSuccess, $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

            using var db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;
            Assert.True(TableExists(db, "LockPermissions"));

            var serviceInstallId = db.QueryRows("SELECT `ServiceInstall` FROM `ServiceInstall`", 1).Value
                .Select(r => r[0]!)
                .Single();

            var row = db.QueryRows(
                    "SELECT `LockObject`, `Table`, `Domain`, `User`, `Permission` FROM `LockPermissions`", 5).Value
                .Single();

            Assert.Equal(serviceInstallId, row[0]);
            Assert.Equal("ServiceInstall", row[1]);
            Assert.Equal("BUILTIN", row[2]);
            Assert.Equal("Administrators", row[3]);
            Assert.Equal(0x000F01FF, int.Parse(row[4]!, CultureInfo.InvariantCulture));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Compile_ServiceWithSddlPermission_EmitsMsiLockPermissionsExRowKeyedByServiceInstallId()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceFile = Path.Combine(tempDir, "svc.exe");
            File.WriteAllText(sourceFile, "fake service executable");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "SvcSddlPermApp";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(1, 0, 0);
                p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "SvcSddlPermApp"));
                p.Service("MyService", svc =>
                {
                    svc.DisplayName = "My Service";
                    svc.Executable = "svc.exe";
                    svc.Permission(perm => perm.Sddl = "D:(A;;RPWP;;;WD)");
                });
            });

            var fileSystem = new WindowsFileSystem();
            var compileResult = new MsiCompiler(fileSystem).Compile(package, outputDir);
            Assert.True(compileResult.IsSuccess, $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

            using var db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;
            Assert.True(TableExists(db, "MsiLockPermissionsEx"));

            var serviceInstallId = db.QueryRows("SELECT `ServiceInstall` FROM `ServiceInstall`", 1).Value
                .Select(r => r[0]!)
                .Single();

            var row = db.QueryRows(
                    "SELECT `LockObject`, `Table`, `SDDLText` FROM `MsiLockPermissionsEx`", 3).Value
                .Single();

            Assert.Equal(serviceInstallId, row[0]);
            Assert.Equal("ServiceInstall", row[1]);
            Assert.Equal("D:(A;;RPWP;;;WD)", row[2]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Compile_InstallDirectory_LeafDirectoryNamedInstallDirNotGeneratedId()
    {
        // Bug: TableEmitter named the leaf install directory with a generated
        // D_* hash ID and pointed a Property named INSTALLDIR at that ID. MSI
        // Formatted strings then resolved "[INSTALLDIR]bin\tool.exe" to the
        // raw directory-ID token rather than the installed path because the
        // Formatted evaluator does not recursively resolve a property value
        // that is itself a directory-ID. The canonical MSI pattern is to name
        // the leaf Directory row "INSTALLDIR" directly so the evaluator does
        // the path resolution in one step.
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceFile = Path.Combine(tempDir, "tool.exe");
            File.WriteAllText(sourceFile, "fake tool content");
            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var installDir = KnownFolder.ProgramFiles / "TestCorp" / "PlugWarden";
            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "PlugWarden";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(1, 0, 0);
                p.DefaultInstallDirectory = installDir;
                p.Files(f => f.Add(sourceFile).To(installDir));
            });

            var fileSystem = new WindowsFileSystem();
            var compileResult = new MsiCompiler(fileSystem).Compile(package, outputDir);
            Assert.True(compileResult.IsSuccess, $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

            using var db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;

            // 1. Directory table must contain a row named "INSTALLDIR".
            var directoryRows = db.QueryRows("SELECT `Directory` FROM `Directory`", 1).Value
                .Select(r => r[0]!)
                .ToHashSet(StringComparer.Ordinal);
            Assert.Contains("INSTALLDIR", directoryRows);

            // 2. No Property row named INSTALLDIR whose value is a generated D_* ID.
            //    (A canonical installer either has no such property at all, or carries
            //    a path override — never a pointer to another Directory row's primary key.)
            var propertyRows = db.QueryRows("SELECT `Property`, `Value` FROM `Property`", 2).Value;
            foreach (var row in propertyRows)
            {
                if (!string.Equals(row[0], "INSTALLDIR", StringComparison.Ordinal))
                    continue;
                Assert.False(
                    row[1]!.StartsWith("D_", StringComparison.Ordinal),
                    $"Property 'INSTALLDIR' points at a generated directory ID '{row[1]}' instead of a real path; formatted strings like [INSTALLDIR]bin will not resolve.");
            }

            // 3. Components installed into the configured install folder must reference
            //    the INSTALLDIR directory key, not a generated D_* leaf ID.
            var componentDirs = db.QueryRows("SELECT `Component`, `Directory_` FROM `Component`", 2).Value;
            Assert.Contains(componentDirs, r => string.Equals(r[1], "INSTALLDIR", StringComparison.Ordinal));
            foreach (var row in componentDirs)
            {
                var dirRef = row[1]!;
                Assert.False(
                    dirRef.StartsWith("D_PlugWarden_", StringComparison.Ordinal),
                    $"Component '{row[0]}' references generated leaf id '{dirRef}' for the install directory; expected 'INSTALLDIR'.");
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    private static bool TableExists(MsiDatabase db, string tableName)
    {
        return db.Execute($"SELECT * FROM `{tableName}`").IsSuccess;
    }

    private static void AssertTableExists(MsiDatabase db, string tableName)
    {
        var result = db.Execute($"SELECT * FROM `{tableName}`");
        Assert.True(result.IsSuccess, $"Table '{tableName}' not found in MSI database: {(result.IsFailure ? result.Error.Message : "")}");
    }

    [Fact]
    public void Compile_MediaTemplate_EmbedCabinetFalse_WritesCabNextToMsi()
    {
        // Gap 4: When EmbedCabinet(false) is set, the Media row references the cab
        // without the '#' prefix (external cab), but previously the compiler never
        // wrote the .cab to disk, so the MSI installed-time lookup failed with
        // "cabinet not found". The fix must drop the cab next to the .msi and
        // keep the Media row clean (no '#' prefix, matching file name).
        var tempDir = Path.Combine(Path.GetTempPath(), $"MsiTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceFile = Path.Combine(tempDir, "payload.bin");
            var payloadBytes = new byte[32 * 1024];
            new Random(54321).NextBytes(payloadBytes);
            File.WriteAllBytes(sourceFile, payloadBytes);

            var outputDir = Path.Combine(tempDir, "output");
            Directory.CreateDirectory(outputDir);

            var package = InstallerTestHost.BuildPackage(p =>
            {
                p.Name = "ExternalCabApp";
                p.Manufacturer = "TestCorp";
                p.Version = new Version(1, 0, 0);
                p.Files(f => f.Add(sourceFile).To(KnownFolder.ProgramFiles / "TestCorp" / "ExternalCabApp"));
                p.MediaTemplate(mt => mt
                    .CabinetTemplate("cab{0}.cab")
                    .EmbedCabinet(false));
            });

            var fileSystem = new WindowsFileSystem();
            var compileResult = new MsiCompiler(fileSystem).Compile(package, outputDir);
            Assert.True(compileResult.IsSuccess,
                $"Compile failed: {(compileResult.IsFailure ? compileResult.Error.Message : "")}");

            var expectedCabPath = Path.Combine(outputDir, "cab1.cab");
            Assert.True(File.Exists(expectedCabPath),
                $"Expected external cabinet '{expectedCabPath}' to exist on disk next to the MSI.");

            // Round-trip the cab through the extractor to prove it is a real, readable cabinet
            // containing the original payload bytes.
            using (var cabStream = File.OpenRead(expectedCabPath))
            {
                var extracted = CabinetExtractor.Extract(cabStream);
                Assert.True(extracted.IsSuccess,
                    $"External cab extraction failed: {(extracted.IsFailure ? extracted.Error.Message : "")}");
                Assert.Single(extracted.Value);
                var entry = Assert.Single(extracted.Value);
                Assert.Equal(payloadBytes, entry.Value);
            }

            using var db = MsiDatabase.Open(compileResult.Value, readOnly: true).Value;

            var mediaCabs = db.QueryRows("SELECT `Cabinet` FROM `Media`", 1).Value
                .Select(r => r[0]!)
                .ToList();

            var mediaCab = Assert.Single(mediaCabs);
            Assert.False(mediaCab.StartsWith('#'),
                $"Media.Cabinet must be external (no '#' prefix) but got '{mediaCab}'.");
            Assert.Equal("cab1.cab", mediaCab);

            // The external cab must NOT be embedded in the _Streams table — that would
            // bloat the MSI and contradict the Media row's external reference.
            var streamsResult = db.QueryRows("SELECT `Name` FROM `_Streams`", 1);
            if (streamsResult.IsSuccess)
            {
                var streamNames = streamsResult.Value.Select(r => r[0]!);
                Assert.DoesNotContain("cab1.cab", streamNames);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
