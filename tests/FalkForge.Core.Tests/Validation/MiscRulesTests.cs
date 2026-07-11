using FalkForge.Models;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Core.Tests.Validation;

/// <summary>
/// Per-rule isolated tests for MiscRules:
/// SHC001-003, FNT001, INI001-003, PRM001-004, FAS001-003,
/// REG007, RRG001-003, RMF001-002, CRF001, MVF001-003, DPF001.
/// </summary>
public sealed class MiscRulesTests
{
    private static RuleContext Ctx(PackageModel pkg) => RuleContext.ForTest(pkg);

    private static PackageModel Base(
        IReadOnlyList<ShortcutModel>? shortcuts = null,
        IReadOnlyList<FontModel>? fonts = null,
        IReadOnlyList<IniFileModel>? iniFiles = null,
        IReadOnlyList<PermissionModel>? permissions = null,
        IReadOnlyList<FileAssociationModel>? fileAssociations = null,
        IReadOnlyList<RegistryEntryModel>? registryEntries = null,
        IReadOnlyList<RemoveRegistryModel>? removeRegistryEntries = null,
        IReadOnlyList<RemoveFileModel>? removeFiles = null,
        IReadOnlyList<CreateFolderModel>? createFolders = null,
        IReadOnlyList<MoveFileModel>? moveFiles = null,
        IReadOnlyList<DuplicateFileModel>? duplicateFiles = null) => new()
    {
        Name = "App",
        Manufacturer = "Corp",
        Version = new Version(1, 0, 0),
        UpgradeCode = Guid.NewGuid(),
        ProductCode = Guid.NewGuid(),
        Shortcuts = shortcuts ?? [],
        Fonts = fonts ?? [],
        IniFiles = iniFiles ?? [],
        Permissions = permissions ?? [],
        FileAssociations = fileAssociations ?? [],
        RegistryEntries = registryEntries ?? [],
        RemoveRegistryEntries = removeRegistryEntries ?? [],
        RemoveFiles = removeFiles ?? [],
        CreateFolders = createFolders ?? [],
        MoveFiles = moveFiles ?? [],
        DuplicateFiles = duplicateFiles ?? []
    };

    // ── SHC001 — Shortcut Name required ─────────────────────────────────────

    [Fact]
    public void Shc001_empty_name_yields_error()
    {
        var pkg = Base(shortcuts: [new ShortcutModel { Name = "", TargetFile = "t.exe" }]);
        var violations = MiscRules.Shc001_NameRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("SHC001", violations[0].RuleId.Value);
        Assert.Equal(Severity.Error, violations[0].Severity);
    }

    [Fact]
    public void Shc001_valid_name_yields_no_violations()
    {
        var pkg = Base(shortcuts: [new ShortcutModel { Name = "MyApp", TargetFile = "t.exe" }]);
        Assert.Empty(MiscRules.Shc001_NameRequired.Evaluate(Ctx(pkg)));
    }

    // ── SHC002 — Shortcut TargetFile required ───────────────────────────────

    [Fact]
    public void Shc002_empty_target_file_yields_error()
    {
        var pkg = Base(shortcuts: [new ShortcutModel { Name = "MyApp", TargetFile = "" }]);
        var violations = MiscRules.Shc002_TargetFileRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("SHC002", violations[0].RuleId.Value);
    }

    [Fact]
    public void Shc002_valid_target_file_yields_no_violations()
    {
        var pkg = Base(shortcuts: [new ShortcutModel { Name = "A", TargetFile = "a.exe" }]);
        Assert.Empty(MiscRules.Shc002_TargetFileRequired.Evaluate(Ctx(pkg)));
    }

    // ── SHC003 — Shortcut locations warning ─────────────────────────────────

    [Fact]
    public void Shc003_no_locations_yields_warning()
    {
        var pkg = Base(shortcuts: [new ShortcutModel { Name = "A", TargetFile = "a.exe" }]);
        var violations = MiscRules.Shc003_LocationsWarning.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("SHC003", violations[0].RuleId.Value);
        Assert.Equal(Severity.Warning, violations[0].Severity);
    }

    [Fact]
    public void Shc003_with_location_yields_no_violations()
    {
        var pkg = Base(shortcuts: [new ShortcutModel
        {
            Name = "A",
            TargetFile = "a.exe",
            Locations = [ShortcutLocation.Desktop]
        }]);
        Assert.Empty(MiscRules.Shc003_LocationsWarning.Evaluate(Ctx(pkg)));
    }

    // ── SHC004 — Shortcut WorkingDirectory identifier warning ───────────────

    [Fact]
    public void Shc004_formatted_path_working_directory_yields_warning()
    {
        // The exact realistic mistake: a bracketed Formatted path (as used for
        // Target) is not a Directory-table key and would be silently ignored.
        var pkg = Base(shortcuts: [new ShortcutModel
        {
            Name = "A",
            TargetFile = "a.exe",
            Locations = [ShortcutLocation.Startup],
            WorkingDirectory = @"[ProgramFilesFolder]Falk Software\App"
        }]);
        var violations = MiscRules.Shc004_WorkingDirectoryIdentifier.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("SHC004", violations[0].RuleId.Value);
        Assert.Equal(Severity.Warning, violations[0].Severity);
    }

    [Fact]
    public void Shc004_directory_identifier_working_directory_yields_no_violations()
    {
        var pkg = Base(shortcuts: [new ShortcutModel
        {
            Name = "A",
            TargetFile = "a.exe",
            Locations = [ShortcutLocation.Startup],
            WorkingDirectory = "INSTALLDIR"
        }]);
        Assert.Empty(MiscRules.Shc004_WorkingDirectoryIdentifier.Evaluate(Ctx(pkg)));
    }

    [Fact]
    public void Shc004_absent_working_directory_yields_no_violations()
    {
        var pkg = Base(shortcuts: [new ShortcutModel
        {
            Name = "A",
            TargetFile = "a.exe",
            Locations = [ShortcutLocation.Startup]
        }]);
        Assert.Empty(MiscRules.Shc004_WorkingDirectoryIdentifier.Evaluate(Ctx(pkg)));
    }

    // ── FNT001 — Font FileName required ─────────────────────────────────────

    [Fact]
    public void Fnt001_empty_filename_yields_error()
    {
        var pkg = Base(fonts: [new FontModel { FileName = "" }]);
        var violations = MiscRules.Fnt001_FileNameRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("FNT001", violations[0].RuleId.Value);
    }

    [Fact]
    public void Fnt001_valid_filename_yields_no_violations()
    {
        var pkg = Base(fonts: [new FontModel { FileName = "Arial.ttf" }]);
        Assert.Empty(MiscRules.Fnt001_FileNameRequired.Evaluate(Ctx(pkg)));
    }

    // ── INI001 — INI FileName required ───────────────────────────────────────

    [Fact]
    public void Ini001_empty_filename_yields_error()
    {
        var pkg = Base(iniFiles: [new IniFileModel { FileName = "", Section = "S", Key = "K", Value = "V" }]);
        var violations = MiscRules.Ini001_FileNameRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("INI001", violations[0].RuleId.Value);
    }

    // ── INI002 — INI Section required ────────────────────────────────────────

    [Fact]
    public void Ini002_empty_section_yields_error()
    {
        var pkg = Base(iniFiles: [new IniFileModel { FileName = "app.ini", Section = "", Key = "K", Value = "V" }]);
        var violations = MiscRules.Ini002_SectionRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("INI002", violations[0].RuleId.Value);
    }

    // ── INI003 — INI Key required ─────────────────────────────────────────────

    [Fact]
    public void Ini003_empty_key_yields_error()
    {
        var pkg = Base(iniFiles: [new IniFileModel { FileName = "app.ini", Section = "S", Key = "", Value = "V" }]);
        var violations = MiscRules.Ini003_KeyRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("INI003", violations[0].RuleId.Value);
    }

    [Fact]
    public void Ini_all_valid_yields_no_violations()
    {
        var pkg = Base(iniFiles: [new IniFileModel { FileName = "app.ini", Section = "App", Key = "InstallDir", Value = @"C:\App" }]);
        Assert.Empty(MiscRules.Ini001_FileNameRequired.Evaluate(Ctx(pkg)));
        Assert.Empty(MiscRules.Ini002_SectionRequired.Evaluate(Ctx(pkg)));
        Assert.Empty(MiscRules.Ini003_KeyRequired.Evaluate(Ctx(pkg)));
    }

    // ── PRM001 — Permission LockObject required ───────────────────────────────

    [Fact]
    public void Prm001_empty_lock_object_yields_error()
    {
        var pkg = Base(permissions: [new PermissionModel { LockObject = "", Table = "File", Sddl = "D:(A;;FA;;;WD)" }]);
        var violations = MiscRules.Prm001_LockObjectRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("PRM001", violations[0].RuleId.Value);
    }

    // ── PRM002 — Permission must have SDDL or User ───────────────────────────

    [Fact]
    public void Prm002_no_sddl_no_user_yields_error()
    {
        var pkg = Base(permissions: [new PermissionModel { LockObject = "file1", Table = "File" }]);
        var violations = MiscRules.Prm002_SddlOrUserRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("PRM002", violations[0].RuleId.Value);
    }

    [Fact]
    public void Prm002_with_sddl_yields_no_violations()
    {
        var pkg = Base(permissions: [new PermissionModel { LockObject = "file1", Table = "File", Sddl = "D:(A;;FA;;;WD)" }]);
        Assert.Empty(MiscRules.Prm002_SddlOrUserRequired.Evaluate(Ctx(pkg)));
    }

    // ── PRM003 — Permission Table must be valid ───────────────────────────────

    [Fact]
    public void Prm003_invalid_table_yields_error()
    {
        var pkg = Base(permissions: [new PermissionModel { LockObject = "obj1", Table = "CustomTable", Sddl = "D:(A;;FA;;;WD)" }]);
        var violations = MiscRules.Prm003_TableValid.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("PRM003", violations[0].RuleId.Value);
    }

    [Fact]
    public void Prm003_valid_tables_yield_no_violations()
    {
        foreach (var table in new[] { "File", "Registry", "CreateFolder", "ServiceInstall" })
        {
            var pkg = Base(permissions: [new PermissionModel { LockObject = "obj", Table = table, Sddl = "D:(A;;FA;;;WD)" }]);
            Assert.Empty(MiscRules.Prm003_TableValid.Evaluate(Ctx(pkg)));
        }
    }

    // ── PRM004 — Cannot mix SDDL and User permissions ────────────────────────

    [Fact]
    public void Prm004_mixed_sddl_and_user_yields_error()
    {
        var pkg = Base(permissions:
        [
            new PermissionModel { LockObject = "f1", Table = "File", Sddl = "D:(A;;FA;;;WD)" },
            new PermissionModel { LockObject = "f2", Table = "File", User = "Everyone" }
        ]);
        var violations = MiscRules.Prm004_NoMixedPermissionTypes.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("PRM004", violations[0].RuleId.Value);
    }

    [Fact]
    public void Prm004_only_sddl_yields_no_violations()
    {
        var pkg = Base(permissions:
        [
            new PermissionModel { LockObject = "f1", Table = "File", Sddl = "D:(A;;FA;;;WD)" },
            new PermissionModel { LockObject = "f2", Table = "File", Sddl = "D:(A;;FA;;;BA)" }
        ]);
        Assert.Empty(MiscRules.Prm004_NoMixedPermissionTypes.Evaluate(Ctx(pkg)));
    }

    // ── FAS001 — File association Extension required ──────────────────────────

    [Fact]
    public void Fas001_empty_extension_yields_error()
    {
        var pkg = Base(fileAssociations: [new FileAssociationModel { Extension = "", ProgId = "P" }]);
        var violations = MiscRules.Fas001_ExtensionRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("FAS001", violations[0].RuleId.Value);
    }

    // ── FAS002 — File association ProgId required ─────────────────────────────

    [Fact]
    public void Fas002_empty_progid_yields_error()
    {
        var pkg = Base(fileAssociations: [new FileAssociationModel { Extension = ".txt", ProgId = "" }]);
        var violations = MiscRules.Fas002_ProgIdRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("FAS002", violations[0].RuleId.Value);
    }

    // ── FAS003 — File association verbs warning ───────────────────────────────

    [Fact]
    public void Fas003_no_verbs_yields_warning()
    {
        var pkg = Base(fileAssociations: [new FileAssociationModel { Extension = ".txt", ProgId = "Txt" }]);
        var violations = MiscRules.Fas003_VerbsWarning.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("FAS003", violations[0].RuleId.Value);
        Assert.Equal(Severity.Warning, violations[0].Severity);
    }

    [Fact]
    public void Fas003_with_verbs_yields_no_violations()
    {
        var pkg = Base(fileAssociations:
        [
            new FileAssociationModel
            {
                Extension = ".txt",
                ProgId = "Txt",
                Verbs = [new VerbModel { Verb = "open", Command = "notepad.exe \"%1\"" }]
            }
        ]);
        Assert.Empty(MiscRules.Fas003_VerbsWarning.Evaluate(Ctx(pkg)));
    }

    // ── REG007 — Sensitive property in registry value ─────────────────────────

    [Fact]
    public void Reg007_password_property_reference_yields_warning()
    {
        var pkg = Base(registryEntries:
        [
            new RegistryEntryModel
            {
                Root = RegistryRoot.LocalMachine,
                Key = @"SOFTWARE\App",
                ValueName = "Pwd",
                Value = "[MY_PASSWORD]"
            }
        ]);
        var violations = MiscRules.Reg007_SensitivePropertyInRegistry.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("REG007", violations[0].RuleId.Value);
        Assert.Equal(Severity.Warning, violations[0].Severity);
    }

    [Fact]
    public void Reg007_non_sensitive_value_yields_no_violations()
    {
        var pkg = Base(registryEntries:
        [
            new RegistryEntryModel
            {
                Root = RegistryRoot.LocalMachine,
                Key = @"SOFTWARE\App",
                ValueName = "Path",
                Value = "[INSTALLFOLDER]"
            }
        ]);
        Assert.Empty(MiscRules.Reg007_SensitivePropertyInRegistry.Evaluate(Ctx(pkg)));
    }

    [Fact]
    public void Reg007_non_string_value_yields_no_violations()
    {
        var pkg = Base(registryEntries:
        [
            new RegistryEntryModel
            {
                Root = RegistryRoot.LocalMachine,
                Key = @"SOFTWARE\App",
                ValueName = "Count",
                Value = 42
            }
        ]);
        Assert.Empty(MiscRules.Reg007_SensitivePropertyInRegistry.Evaluate(Ctx(pkg)));
    }

    // ── RRG001 — RemoveRegistry Id required ──────────────────────────────────

    [Fact]
    public void Rrg001_empty_id_yields_error()
    {
        var pkg = Base(removeRegistryEntries:
        [
            new RemoveRegistryModel { Id = "", Root = RegistryRoot.LocalMachine, Key = @"SOFTWARE\App" }
        ]);
        var violations = MiscRules.Rrg001_IdRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("RRG001", violations[0].RuleId.Value);
    }

    // ── RRG002 — RemoveRegistry Key required ──────────────────────────────────

    [Fact]
    public void Rrg002_empty_key_yields_error()
    {
        var pkg = Base(removeRegistryEntries:
        [
            new RemoveRegistryModel { Id = "RR1", Root = RegistryRoot.LocalMachine, Key = "" }
        ]);
        var violations = MiscRules.Rrg002_KeyRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("RRG002", violations[0].RuleId.Value);
    }

    // ── RRG003 — RemoveValue action requires Name ─────────────────────────────

    [Fact]
    public void Rrg003_remove_value_without_name_yields_error()
    {
        var pkg = Base(removeRegistryEntries:
        [
            new RemoveRegistryModel
            {
                Id = "RR1",
                Root = RegistryRoot.LocalMachine,
                Key = @"SOFTWARE\App",
                Action = RemoveRegistryAction.RemoveValue,
                Name = null
            }
        ]);
        var violations = MiscRules.Rrg003_RemoveValueRequiresName.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("RRG003", violations[0].RuleId.Value);
    }

    [Fact]
    public void Rrg003_remove_value_with_name_yields_no_violations()
    {
        var pkg = Base(removeRegistryEntries:
        [
            new RemoveRegistryModel
            {
                Id = "RR1",
                Root = RegistryRoot.LocalMachine,
                Key = @"SOFTWARE\App",
                Action = RemoveRegistryAction.RemoveValue,
                Name = "MyValue"
            }
        ]);
        Assert.Empty(MiscRules.Rrg003_RemoveValueRequiresName.Evaluate(Ctx(pkg)));
    }

    // ── RMF001 — RemoveFile DirectoryRef required ─────────────────────────────

    [Fact]
    public void Rmf001_empty_directory_ref_yields_error()
    {
        var pkg = Base(removeFiles: [new RemoveFileModel { Id = "RF1", DirectoryRef = "", OnUninstall = true }]);
        var violations = MiscRules.Rmf001_DirectoryRefRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("RMF001", violations[0].RuleId.Value);
    }

    // ── RMF002 — RemoveFile must specify OnInstall or OnUninstall ─────────────

    [Fact]
    public void Rmf002_neither_on_install_nor_on_uninstall_yields_error()
    {
        var pkg = Base(removeFiles: [new RemoveFileModel { Id = "RF1", DirectoryRef = "INSTALLFOLDER" }]);
        var violations = MiscRules.Rmf002_InstallOrUninstallRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("RMF002", violations[0].RuleId.Value);
    }

    [Fact]
    public void Rmf002_on_uninstall_only_yields_no_violations()
    {
        var pkg = Base(removeFiles: [new RemoveFileModel { Id = "RF1", DirectoryRef = "INSTALLFOLDER", OnUninstall = true }]);
        Assert.Empty(MiscRules.Rmf002_InstallOrUninstallRequired.Evaluate(Ctx(pkg)));
    }

    // ── CRF001 — CreateFolder DirectoryRef required ───────────────────────────

    [Fact]
    public void Crf001_empty_directory_ref_yields_error()
    {
        var pkg = Base(createFolders: [new CreateFolderModel { Id = "CF1", DirectoryRef = "" }]);
        var violations = MiscRules.Crf001_DirectoryRefRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("CRF001", violations[0].RuleId.Value);
    }

    [Fact]
    public void Crf001_valid_directory_ref_yields_no_violations()
    {
        var pkg = Base(createFolders: [new CreateFolderModel { Id = "CF1", DirectoryRef = "LOGFOLDER" }]);
        Assert.Empty(MiscRules.Crf001_DirectoryRefRequired.Evaluate(Ctx(pkg)));
    }

    // ── MVF001 — MoveFile SourceDirectory required ────────────────────────────

    [Fact]
    public void Mvf001_empty_source_directory_yields_error()
    {
        var pkg = Base(moveFiles: [new MoveFileModel { Id = "MF1", SourceDirectory = "", SourceFileName = "f.txt", DestDirectory = "D" }]);
        var violations = MiscRules.Mvf001_SourceDirectoryRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("MVF001", violations[0].RuleId.Value);
    }

    // ── MVF002 — MoveFile SourceFileName required ─────────────────────────────

    [Fact]
    public void Mvf002_empty_source_filename_yields_error()
    {
        var pkg = Base(moveFiles: [new MoveFileModel { Id = "MF1", SourceDirectory = "S", SourceFileName = "", DestDirectory = "D" }]);
        var violations = MiscRules.Mvf002_SourceFileNameRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("MVF002", violations[0].RuleId.Value);
    }

    // ── MVF003 — MoveFile DestDirectory required ──────────────────────────────

    [Fact]
    public void Mvf003_empty_dest_directory_yields_error()
    {
        var pkg = Base(moveFiles: [new MoveFileModel { Id = "MF1", SourceDirectory = "S", SourceFileName = "f.txt", DestDirectory = "" }]);
        var violations = MiscRules.Mvf003_DestDirectoryRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("MVF003", violations[0].RuleId.Value);
    }

    [Fact]
    public void Mvf_all_valid_yields_no_violations()
    {
        var pkg = Base(moveFiles: [new MoveFileModel { Id = "MF1", SourceDirectory = "SRC", SourceFileName = "f.txt", DestDirectory = "DEST" }]);
        Assert.Empty(MiscRules.Mvf001_SourceDirectoryRequired.Evaluate(Ctx(pkg)));
        Assert.Empty(MiscRules.Mvf002_SourceFileNameRequired.Evaluate(Ctx(pkg)));
        Assert.Empty(MiscRules.Mvf003_DestDirectoryRequired.Evaluate(Ctx(pkg)));
    }

    // ── DPF001 — DuplicateFile FileRef required ───────────────────────────────

    [Fact]
    public void Dpf001_empty_file_ref_yields_error()
    {
        var pkg = Base(duplicateFiles: [new DuplicateFileModel { Id = "DF1", FileRef = "" }]);
        var violations = MiscRules.Dpf001_FileRefRequired.Evaluate(Ctx(pkg)).ToList();

        Assert.Single(violations);
        Assert.Equal("DPF001", violations[0].RuleId.Value);
    }

    [Fact]
    public void Dpf001_valid_file_ref_yields_no_violations()
    {
        var pkg = Base(duplicateFiles: [new DuplicateFileModel { Id = "DF1", FileRef = "MainExe" }]);
        Assert.Empty(MiscRules.Dpf001_FileRefRequired.Evaluate(Ctx(pkg)));
    }
}
