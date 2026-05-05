using System.Runtime.Versioning;
using FalkForge.Builders;
using FalkForge.Integration.Tests.RecipeParity;
using FalkForge.Models;
using FalkForge.Testing;
using Xunit;

namespace FalkForge.Integration.Tests.RecipeParity;

/// <summary>
/// Phase 9 Step 5 — comprehensive byte-diff sweep covering every integration
/// scenario exercised by <c>MsiCompilerIntegrationTests</c>. Each scenario builds
/// a <see cref="PackageModel"/> and feeds it to
/// <see cref="MsiByteDiffHarness.CompareCompilers"/> to assert that the legacy
/// and recipe pipelines produce structurally identical MSI files (identical table
/// sets and identical row content for every table). Byte-level identity is
/// asserted only for reproducible builds; non-reproducible builds contain
/// wall-clock timestamps that legitimately differ between the two compile calls.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class IntegrationParitySweep
{
    // Fixed Unix epoch shared across reproducibility tests — pins all
    // SummaryInfo FILETIME values so byte-level identity is deterministic.
    private const long TestEpoch = 1577836800L;

    // -------------------------------------------------------------------------
    // 1. Minimal package — no files
    // -------------------------------------------------------------------------

    [Fact]
    public void Sweep_MinimalPackage_NoFiles_StructuralParityAndByteIdentity()
    {
        PackageModel package = new PackageBuilder
        {
            Name = "SweepMinimal",
            Manufacturer = "FalkForge Tests",
            Version = new Version(1, 0, 0),
            ProductCode = Guid.Parse("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"),
            UpgradeCode = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        }.Also(b => b.Reproducible(TestEpoch)).Build();

        MsiDiffReport report = MsiByteDiffHarness.CompareCompilers(
            package, nameof(Sweep_MinimalPackage_NoFiles_StructuralParityAndByteIdentity));

        AssertNoDivergences(report);
    }

    // -------------------------------------------------------------------------
    // 2. Package with a single file
    // -------------------------------------------------------------------------

    [Fact]
    public void Sweep_PackageWithSingleFile_StructuralParity()
    {
        string tmpDir = MakeTempDir();
        try
        {
            string srcFile = WriteFile(tmpDir, "app.exe", "fake executable content for testing");

            PackageModel package = BuildReproduciblePackage(
                "SweepSingleFile",
                Guid.Parse("BBBBBBBB-CCCC-DDDD-EEEE-FFFFFFFFFFFF"),
                Guid.Parse("22222222-3333-4444-5555-666666666666"),
                b =>
                {
                    b.Files(f => f.Add(srcFile).To(KnownFolder.ProgramFiles / "TestCorp" / "SweepSingleFile"));
                });

            MsiDiffReport report = MsiByteDiffHarness.CompareCompilers(
                package, nameof(Sweep_PackageWithSingleFile_StructuralParity));

            AssertNoStructuralDivergences(report);
        }
        finally
        {
            TryDeleteDir(tmpDir);
        }
    }

    // -------------------------------------------------------------------------
    // 3. Package with subdirectory harvest (tests Directory table depth >= 2)
    // -------------------------------------------------------------------------

    [Fact]
    public void Sweep_PackageWithSubdirectory_DirectoryTableParity()
    {
        string tmpDir = MakeTempDir();
        try
        {
            string serviceDir = Path.Combine(tmpDir, "Service");
            string pluginsDir = Path.Combine(serviceDir, "Plugins");
            Directory.CreateDirectory(pluginsDir);
            File.WriteAllText(Path.Combine(serviceDir, "PlugWarden.Service.exe"), "fake service");
            File.WriteAllText(Path.Combine(pluginsDir, "plugin.dll"), "fake plugin");

            var installDir = KnownFolder.ProgramFiles / "TestCorp" / "HarvestApp";
            PackageModel package = BuildReproduciblePackage(
                "SweepHarvest",
                Guid.Parse("CCCCCCCC-DDDD-EEEE-FFFF-000000000000"),
                Guid.Parse("33333333-4444-5555-6666-777777777777"),
                b =>
                {
                    b.DefaultInstallDirectory = installDir;
                    b.Files(f => f.FromDirectory(tmpDir).To(installDir));
                });

            MsiDiffReport report = MsiByteDiffHarness.CompareCompilers(
                package, nameof(Sweep_PackageWithSubdirectory_DirectoryTableParity));

            AssertNoStructuralDivergences(report);
        }
        finally
        {
            TryDeleteDir(tmpDir);
        }
    }

    // -------------------------------------------------------------------------
    // 4. Package with shortcuts (start menu subfolder, startup, desktop)
    // -------------------------------------------------------------------------

    [Fact]
    public void Sweep_PackageWithShortcuts_ShortcutAndDirectoryTableParity()
    {
        string tmpDir = MakeTempDir();
        try
        {
            string srcFile = WriteFile(tmpDir, "app.exe", "fake");

            PackageModel package = BuildReproduciblePackage(
                "SweepShortcuts",
                Guid.Parse("DDDDDDDD-EEEE-FFFF-0000-111111111111"),
                Guid.Parse("44444444-5555-6666-7777-888888888888"),
                b =>
                {
                    b.Files(f => f.Add(srcFile).To(KnownFolder.ProgramFiles / "TestCorp" / "ShortcutApp"));
                    b.Shortcut("App", "app.exe").OnStartMenu("MyAppFolder");
                    b.Shortcut("App Autostart", "app.exe").OnStartup();
                    b.Shortcut("App Desktop", "app.exe").OnDesktop();
                });

            MsiDiffReport report = MsiByteDiffHarness.CompareCompilers(
                package, nameof(Sweep_PackageWithShortcuts_ShortcutAndDirectoryTableParity));

            AssertNoStructuralDivergences(report);
        }
        finally
        {
            TryDeleteDir(tmpDir);
        }
    }

    // -------------------------------------------------------------------------
    // 5. Package with colliding shortcut subfolder names (hash disambiguation)
    // -------------------------------------------------------------------------

    [Fact]
    public void Sweep_PackageWithCollidingShortcutSubfolders_DistinctDirectoryRows()
    {
        string tmpDir = MakeTempDir();
        try
        {
            string srcFile = WriteFile(tmpDir, "app.exe", "fake");

            PackageModel package = BuildReproduciblePackage(
                "SweepSubCollision",
                Guid.Parse("EEEEEEEE-FFFF-0000-1111-222222222222"),
                Guid.Parse("55555555-6666-7777-8888-999999999999"),
                b =>
                {
                    b.Files(f => f.Add(srcFile).To(KnownFolder.ProgramFiles / "TestCorp" / "SubCollApp"));
                    b.Shortcut("First", "app.exe").OnStartMenu("My App");
                    b.Shortcut("Second", "app.exe").OnStartMenu("My-App");
                });

            MsiDiffReport report = MsiByteDiffHarness.CompareCompilers(
                package, nameof(Sweep_PackageWithCollidingShortcutSubfolders_DistinctDirectoryRows));

            AssertNoStructuralDivergences(report);
        }
        finally
        {
            TryDeleteDir(tmpDir);
        }
    }

    // -------------------------------------------------------------------------
    // 6. Package with service + two files (service component lookup)
    // -------------------------------------------------------------------------

    [Fact]
    public void Sweep_PackageWithService_ServiceInstallTableParity()
    {
        string tmpDir = MakeTempDir();
        try
        {
            // Two files — sqlite helper sorts before service exe alphabetically
            File.WriteAllText(Path.Combine(tmpDir, "e_sqlite3.dll"), "native helper");
            File.WriteAllText(Path.Combine(tmpDir, "PlugWarden.Service.exe"), "service exe");

            var installDir = KnownFolder.ProgramFiles / "TestCorp" / "SvcApp" / "Service";
            PackageModel package = BuildReproduciblePackage(
                "SweepSvcApp",
                Guid.Parse("FFFFFFFF-0000-1111-2222-333333333333"),
                Guid.Parse("66666666-7777-8888-9999-AAAAAAAAAAAA"),
                b =>
                {
                    b.DefaultInstallDirectory = installDir;
                    b.Files(f => f.FromDirectory(tmpDir).To(installDir));
                    b.Service("MyService", svc =>
                    {
                        svc.DisplayName = "My Service";
                        svc.Executable = "[INSTALLFOLDER]Service\\PlugWarden.Service.exe";
                        svc.StartMode = ServiceStartMode.Automatic;
                        svc.Account = ServiceAccount.LocalSystem;
                    });
                });

            MsiDiffReport report = MsiByteDiffHarness.CompareCompilers(
                package, nameof(Sweep_PackageWithService_ServiceInstallTableParity));

            AssertNoStructuralDivergences(report);
        }
        finally
        {
            TryDeleteDir(tmpDir);
        }
    }

    // -------------------------------------------------------------------------
    // 7. MediaTemplate — embedded cabinet (single)
    // -------------------------------------------------------------------------

    [Fact]
    public void Sweep_MediaTemplate_EmbeddedSingleCab_MediaTableParity()
    {
        string tmpDir = MakeTempDir();
        try
        {
            string srcFile = WriteFile(tmpDir, "app.exe", "fake content for cab stream test");

            PackageModel package = BuildReproduciblePackage(
                "SweepCabEmbed",
                Guid.Parse("00000001-1111-2222-3333-444444444444"),
                Guid.Parse("77777777-8888-9999-AAAA-BBBBBBBBBBBB"),
                b =>
                {
                    b.Files(f => f.Add(srcFile).To(KnownFolder.ProgramFiles / "TestCorp" / "CabNameApp"));
                    b.MediaTemplate(mt => mt.CabinetTemplate("data{0}.cab").EmbedCabinet(true));
                });

            MsiDiffReport report = MsiByteDiffHarness.CompareCompilers(
                package, nameof(Sweep_MediaTemplate_EmbeddedSingleCab_MediaTableParity));

            AssertNoStructuralDivergences(report);
        }
        finally
        {
            TryDeleteDir(tmpDir);
        }
    }

    // -------------------------------------------------------------------------
    // 8. MediaTemplate — external cabinet
    // -------------------------------------------------------------------------

    [Fact]
    public void Sweep_MediaTemplate_ExternalCab_MediaTableParity()
    {
        string tmpDir = MakeTempDir();
        try
        {
            byte[] payload = new byte[32 * 1024];
            new Random(54321).NextBytes(payload);
            string srcFile = Path.Combine(tmpDir, "payload.bin");
            File.WriteAllBytes(srcFile, payload);

            PackageModel package = BuildReproduciblePackage(
                "SweepExternalCab",
                Guid.Parse("00000002-1111-2222-3333-444444444444"),
                Guid.Parse("88888888-9999-AAAA-BBBB-CCCCCCCCCCCC"),
                b =>
                {
                    b.Files(f => f.Add(srcFile).To(KnownFolder.ProgramFiles / "TestCorp" / "ExternalCabApp"));
                    b.MediaTemplate(mt => mt.CabinetTemplate("cab{0}.cab").EmbedCabinet(false));
                });

            MsiDiffReport report = MsiByteDiffHarness.CompareCompilers(
                package, nameof(Sweep_MediaTemplate_ExternalCab_MediaTableParity));

            AssertNoStructuralDivergences(report);
        }
        finally
        {
            TryDeleteDir(tmpDir);
        }
    }

    // -------------------------------------------------------------------------
    // 9. Multi-cabinet split (payload exceeds MaxCabinetSizeInMB)
    // -------------------------------------------------------------------------

    [Fact]
    public void Sweep_MultiCabinetSplit_AllMediaRowsPresent()
    {
        string tmpDir = MakeTempDir();
        try
        {
            byte[] payload = new byte[500 * 1024];
            new Random(12345).NextBytes(payload);
            var targetDir = KnownFolder.ProgramFiles / "TestCorp" / "MultiCab";

            PackageModel package = BuildReproduciblePackage(
                "SweepMultiCab",
                Guid.Parse("00000003-1111-2222-3333-444444444444"),
                Guid.Parse("99999999-AAAA-BBBB-CCCC-DDDDDDDDDDDD"),
                b =>
                {
                    b.DefaultInstallDirectory = targetDir;
                    b.MediaTemplate(mt => mt
                        .CabinetTemplate("data{0}.cab")
                        .MaxCabinetSizeMB(1)
                        .EmbedCabinet(true));
                    for (int i = 0; i < 3; i++)
                    {
                        string path = Path.Combine(tmpDir, $"payload{i}.bin");
                        File.WriteAllBytes(path, payload);
                        b.Files(f => f.Add(path).To(targetDir));
                    }
                });

            MsiDiffReport report = MsiByteDiffHarness.CompareCompilers(
                package, nameof(Sweep_MultiCabinetSplit_AllMediaRowsPresent));

            AssertNoStructuralDivergences(report);
        }
        finally
        {
            TryDeleteDir(tmpDir);
        }
    }

    // -------------------------------------------------------------------------
    // 10. SDDL permission — MsiLockPermissionsEx only
    // -------------------------------------------------------------------------

    [Fact]
    public void Sweep_PackageWithSddlPermission_MsiLockPermissionsExParity()
    {
        string tmpDir = MakeTempDir();
        try
        {
            string srcFile = WriteFile(tmpDir, "app.exe", "fake");

            PackageModel package = BuildReproduciblePackage(
                "SweepSddlApp",
                Guid.Parse("00000004-1111-2222-3333-444444444444"),
                Guid.Parse("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"),
                b =>
                {
                    b.Files(f => f.Add(srcFile).To(KnownFolder.ProgramFiles / "TestCorp" / "SddlApp"));
                    b.Permission("SddlApp", perm => { perm.ForTable("File"); perm.Sddl = "D:(A;;RPWP;;;WD)"; });
                });

            MsiDiffReport report = MsiByteDiffHarness.CompareCompilers(
                package, nameof(Sweep_PackageWithSddlPermission_MsiLockPermissionsExParity));

            AssertNoStructuralDivergences(report);
        }
        finally
        {
            TryDeleteDir(tmpDir);
        }
    }

    // -------------------------------------------------------------------------
    // 11. User permission — LockPermissions only
    // -------------------------------------------------------------------------

    [Fact]
    public void Sweep_PackageWithUserPermission_LockPermissionsTableParity()
    {
        string tmpDir = MakeTempDir();
        try
        {
            string srcFile = WriteFile(tmpDir, "app.exe", "fake");

            PackageModel package = BuildReproduciblePackage(
                "SweepUserPerm",
                Guid.Parse("00000005-1111-2222-3333-444444444444"),
                Guid.Parse("BBBBBBBB-CCCC-DDDD-EEEE-FFFFFFFFFFFF"),
                b =>
                {
                    b.Files(f => f.Add(srcFile).To(KnownFolder.ProgramFiles / "TestCorp" / "UserApp"));
                    b.Permission("UserApp", perm =>
                    {
                        perm.ForTable("File");
                        perm.User = "Everyone";
                        perm.Permission = 0x10000000;
                    });
                });

            MsiDiffReport report = MsiByteDiffHarness.CompareCompilers(
                package, nameof(Sweep_PackageWithUserPermission_LockPermissionsTableParity));

            AssertNoStructuralDivergences(report);
        }
        finally
        {
            TryDeleteDir(tmpDir);
        }
    }

    // -------------------------------------------------------------------------
    // 12. Package with INSTALLDIR as leaf directory name
    // -------------------------------------------------------------------------

    [Fact]
    public void Sweep_InstallDirectory_LeafNamedInstallDir_Parity()
    {
        string tmpDir = MakeTempDir();
        try
        {
            string srcFile = WriteFile(tmpDir, "tool.exe", "fake tool");

            var installDir = KnownFolder.ProgramFiles / "TestCorp" / "PlugWarden";
            PackageModel package = BuildReproduciblePackage(
                "SweepPlugWarden",
                Guid.Parse("00000006-1111-2222-3333-444444444444"),
                Guid.Parse("CCCCCCCC-DDDD-EEEE-FFFF-000000000000"),
                b =>
                {
                    b.DefaultInstallDirectory = installDir;
                    b.Files(f => f.Add(srcFile).To(installDir));
                });

            MsiDiffReport report = MsiByteDiffHarness.CompareCompilers(
                package, nameof(Sweep_InstallDirectory_LeafNamedInstallDir_Parity));

            AssertNoStructuralDivergences(report);
        }
        finally
        {
            TryDeleteDir(tmpDir);
        }
    }

    // -------------------------------------------------------------------------
    // 13. MajorUpgrade — default (no allow-same-version)
    // -------------------------------------------------------------------------

    [Fact]
    public void Sweep_MajorUpgrade_Default_UpgradeTableParity()
    {
        string tmpDir = MakeTempDir();
        try
        {
            string srcFile = WriteFile(tmpDir, "app.exe", "fake");

            PackageModel package = BuildReproduciblePackage(
                "SweepMajorUpgrade",
                Guid.Parse("00000007-1111-2222-3333-444444444444"),
                Guid.Parse("DDDDDDDD-EEEE-FFFF-0000-111111111111"),
                b =>
                {
                    b.Files(f => f.Add(srcFile).To(KnownFolder.ProgramFiles / "TestCorp" / "UpgradeApp"));
                    b.MajorUpgrade(_ => { }); // default: AllowSameVersionUpgrades = false
                });

            MsiDiffReport report = MsiByteDiffHarness.CompareCompilers(
                package, nameof(Sweep_MajorUpgrade_Default_UpgradeTableParity));

            AssertNoStructuralDivergences(report);
        }
        finally
        {
            TryDeleteDir(tmpDir);
        }
    }

    // -------------------------------------------------------------------------
    // 14. Registry entries
    // -------------------------------------------------------------------------

    [Fact]
    public void Sweep_PackageWithRegistry_RegistryTableParity()
    {
        string tmpDir = MakeTempDir();
        try
        {
            string srcFile = WriteFile(tmpDir, "app.exe", "fake");

            PackageModel package = BuildReproduciblePackage(
                "SweepRegistry",
                Guid.Parse("00000008-1111-2222-3333-444444444444"),
                Guid.Parse("EEEEEEEE-FFFF-0000-1111-222222222222"),
                b =>
                {
                    b.Files(f => f.Add(srcFile).To(KnownFolder.ProgramFiles / "TestCorp" / "RegApp"));
                    b.Registry(reg =>
                    {
                        reg.Key(RegistryRoot.LocalMachine, @"SOFTWARE\TestCorp\RegApp", k =>
                        {
                            k.Value("InstallPath", "[INSTALLFOLDER]");
                        });
                    });
                });

            MsiDiffReport report = MsiByteDiffHarness.CompareCompilers(
                package, nameof(Sweep_PackageWithRegistry_RegistryTableParity));

            AssertNoStructuralDivergences(report);
        }
        finally
        {
            TryDeleteDir(tmpDir);
        }
    }

    // -------------------------------------------------------------------------
    // 15. IniFile entries
    // -------------------------------------------------------------------------

    [Fact]
    public void Sweep_PackageWithIniFile_IniFileTableParity()
    {
        string tmpDir = MakeTempDir();
        try
        {
            string srcFile = WriteFile(tmpDir, "app.exe", "fake");

            PackageModel package = BuildReproduciblePackage(
                "SweepIniFile",
                Guid.Parse("00000009-1111-2222-3333-444444444444"),
                Guid.Parse("FFFFFFFF-0000-1111-2222-333333333333"),
                b =>
                {
                    b.Files(f => f.Add(srcFile).To(KnownFolder.ProgramFiles / "TestCorp" / "IniApp"));
                    b.IniFile("app.ini", ini =>
                    {
                        ini.Section("Settings")
                           .Key("Mode")
                           .Value("Production")
                           .Action(IniFileAction.CreateEntry);
                    });
                });

            MsiDiffReport report = MsiByteDiffHarness.CompareCompilers(
                package, nameof(Sweep_PackageWithIniFile_IniFileTableParity));

            AssertNoStructuralDivergences(report);
        }
        finally
        {
            TryDeleteDir(tmpDir);
        }
    }

    // -------------------------------------------------------------------------
    // 16. Environment variable
    // -------------------------------------------------------------------------

    [Fact]
    public void Sweep_PackageWithEnvironmentVariable_EnvironmentTableParity()
    {
        string tmpDir = MakeTempDir();
        try
        {
            string srcFile = WriteFile(tmpDir, "app.exe", "fake");

            PackageModel package = BuildReproduciblePackage(
                "SweepEnvVar",
                Guid.Parse("0000000A-1111-2222-3333-444444444444"),
                Guid.Parse("00000000-AAAA-BBBB-CCCC-DDDDDDDDDDDD"),
                b =>
                {
                    b.Files(f => f.Add(srcFile).To(KnownFolder.ProgramFiles / "TestCorp" / "EnvApp"));
                    b.EnvironmentVariable("TESTAPP_HOME", "[INSTALLFOLDER]", env =>
                    {
                        env.Action = EnvironmentVariableAction.Set;
                        env.IsSystem = true;
                    });
                });

            MsiDiffReport report = MsiByteDiffHarness.CompareCompilers(
                package, nameof(Sweep_PackageWithEnvironmentVariable_EnvironmentTableParity));

            AssertNoStructuralDivergences(report);
        }
        finally
        {
            TryDeleteDir(tmpDir);
        }
    }

    // -------------------------------------------------------------------------
    // 17. Custom action
    // -------------------------------------------------------------------------

    [Fact]
    public void Sweep_PackageWithCustomAction_CustomActionTableParity()
    {
        string tmpDir = MakeTempDir();
        try
        {
            string srcFile = WriteFile(tmpDir, "app.exe", "fake");
            string caFile = WriteFile(tmpDir, "customaction.dll", "fake CA dll");

            PackageModel package = BuildReproduciblePackage(
                "SweepCustomAction",
                Guid.Parse("0000000B-1111-2222-3333-444444444444"),
                Guid.Parse("00000000-BBBB-CCCC-DDDD-EEEEEEEEEEEE"),
                b =>
                {
                    b.Files(f => f.Add(srcFile).To(KnownFolder.ProgramFiles / "TestCorp" / "CaApp"));
                    b.CustomAction("SetProperty", ca =>
                    {
                        ca.SetProperty("MYPROP", "MyValue");
                    });
                });

            MsiDiffReport report = MsiByteDiffHarness.CompareCompilers(
                package, nameof(Sweep_PackageWithCustomAction_CustomActionTableParity));

            AssertNoStructuralDivergences(report);
        }
        finally
        {
            TryDeleteDir(tmpDir);
        }
    }

    // -------------------------------------------------------------------------
    // 18. Launch condition
    // -------------------------------------------------------------------------

    [Fact]
    public void Sweep_PackageWithLaunchCondition_LaunchConditionTableParity()
    {
        string tmpDir = MakeTempDir();
        try
        {
            string srcFile = WriteFile(tmpDir, "app.exe", "fake");

            PackageModel package = BuildReproduciblePackage(
                "SweepLaunchCond",
                Guid.Parse("0000000C-1111-2222-3333-444444444444"),
                Guid.Parse("00000000-CCCC-DDDD-EEEE-FFFFFFFFFFFF"),
                b =>
                {
                    b.Files(f => f.Add(srcFile).To(KnownFolder.ProgramFiles / "TestCorp" / "LaunchCondApp"));
                    b.Require(Condition.Is64BitOS, "This application requires a 64-bit operating system.");
                });

            MsiDiffReport report = MsiByteDiffHarness.CompareCompilers(
                package, nameof(Sweep_PackageWithLaunchCondition_LaunchConditionTableParity));

            AssertNoStructuralDivergences(report);
        }
        finally
        {
            TryDeleteDir(tmpDir);
        }
    }

    // -------------------------------------------------------------------------
    // 19. Create folder
    // -------------------------------------------------------------------------

    [Fact]
    public void Sweep_PackageWithCreateFolder_CreateFolderTableParity()
    {
        string tmpDir = MakeTempDir();
        try
        {
            string srcFile = WriteFile(tmpDir, "app.exe", "fake");

            PackageModel package = BuildReproduciblePackage(
                "SweepCreateFolder",
                Guid.Parse("0000000D-1111-2222-3333-444444444444"),
                Guid.Parse("00000000-DDDD-EEEE-FFFF-000000000001"),
                b =>
                {
                    b.Files(f => f.Add(srcFile).To(KnownFolder.ProgramFiles / "TestCorp" / "FolderApp"));
                    b.CreateFolder(cf =>
                    {
                        cf.Directory("LogsFolder")
                          .ComponentRef("FolderApp");
                    });
                });

            MsiDiffReport report = MsiByteDiffHarness.CompareCompilers(
                package, nameof(Sweep_PackageWithCreateFolder_CreateFolderTableParity));

            AssertNoStructuralDivergences(report);
        }
        finally
        {
            TryDeleteDir(tmpDir);
        }
    }

    // -------------------------------------------------------------------------
    // 20. Property override
    // -------------------------------------------------------------------------

    [Fact]
    public void Sweep_PackageWithCustomProperty_PropertyTableParity()
    {
        string tmpDir = MakeTempDir();
        try
        {
            string srcFile = WriteFile(tmpDir, "app.exe", "fake");

            PackageModel package = BuildReproduciblePackage(
                "SweepProperty",
                Guid.Parse("0000000E-1111-2222-3333-444444444444"),
                Guid.Parse("00000000-EEEE-FFFF-0000-000000000001"),
                b =>
                {
                    b.Files(f => f.Add(srcFile).To(KnownFolder.ProgramFiles / "TestCorp" / "PropApp"));
                    b.Property("MYPROPERTY", "MyValue");
                });

            MsiDiffReport report = MsiByteDiffHarness.CompareCompilers(
                package, nameof(Sweep_PackageWithCustomProperty_PropertyTableParity));

            AssertNoStructuralDivergences(report);
        }
        finally
        {
            TryDeleteDir(tmpDir);
        }
    }

    // -------------------------------------------------------------------------
    // 21. Full byte identity — reproducible, with files
    // -------------------------------------------------------------------------

    [Fact]
    public void Sweep_ReproduciblePackageWithFile_ByteIdentical()
    {
        string tmpDir = MakeTempDir();
        try
        {
            string srcFile = Path.Combine(tmpDir, "payload.dll");
            File.WriteAllBytes(srcFile, new byte[] { 0x4D, 0x5A, 0x01, 0x02, 0x03, 0x04 });
            File.SetLastWriteTimeUtc(srcFile, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            PackageModel package = BuildReproduciblePackage(
                "SweepByteIdentity",
                Guid.Parse("0000000F-1111-2222-3333-444444444444"),
                Guid.Parse("00000000-FFFF-0000-1111-000000000001"),
                b =>
                {
                    b.Files(f => f.Add(srcFile).To(KnownFolder.ProgramFiles / "FalkForge Tests" / "ReproTest"));
                });

            MsiDiffReport report = MsiByteDiffHarness.CompareCompilers(
                package, nameof(Sweep_ReproduciblePackageWithFile_ByteIdentical));

            AssertNoDivergences(report);
        }
        finally
        {
            TryDeleteDir(tmpDir);
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a reproducible package with pinned GUIDs. The builder action
    /// configures features, files, and additional options.
    /// </summary>
    private static PackageModel BuildReproduciblePackage(
        string name,
        Guid productCode,
        Guid upgradeCode,
        Action<PackageBuilder> configure)
    {
        var builder = new PackageBuilder
        {
            Name = name,
            Manufacturer = "FalkForge Tests",
            Version = new Version(1, 0, 0),
            ProductCode = productCode,
            UpgradeCode = upgradeCode,
        };
        builder.Reproducible(TestEpoch);
        configure(builder);
        return builder.Build();
    }

    private static void AssertNoDivergences(MsiDiffReport report)
    {
        string diffs = string.Join(Environment.NewLine, report.StructuralDifferences);
        Assert.True(
            report.StructuralDifferences.IsEmpty,
            $"Structural divergences between legacy and recipe compilers:{Environment.NewLine}{diffs}");
        Assert.True(
            report.Equal,
            $"MSI files are not byte-identical.{Environment.NewLine}" +
            $"Structural diffs:{Environment.NewLine}{diffs}" +
            (report.FirstByteDiff is { } bd
                ? $"{Environment.NewLine}First byte diff: {bd}"
                : string.Empty));
    }

    private static void AssertNoStructuralDivergences(MsiDiffReport report)
    {
        string diffs = string.Join(Environment.NewLine, report.StructuralDifferences);
        Assert.True(
            report.StructuralDifferences.IsEmpty,
            $"Structural divergences between legacy and recipe compilers:{Environment.NewLine}{diffs}");
    }

    private static string MakeTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"FalkForge-Sweep-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string WriteFile(string dir, string name, string content)
    {
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static void TryDeleteDir(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Extension for fluent <see cref="PackageBuilder"/> configuration.</summary>
internal static class PackageBuilderExtensions
{
    /// <summary>Returns <paramref name="builder"/> after applying <paramref name="action"/>.</summary>
    public static PackageBuilder Also(this PackageBuilder builder, Action<PackageBuilder> action)
    {
        action(builder);
        return builder;
    }
}
