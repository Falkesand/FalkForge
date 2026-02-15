using FalkInstaller.Builders;
using FalkInstaller.Models;
using FalkInstaller.Testing;
using FalkInstaller.Validation;
using Xunit;

namespace FalkInstaller.Core.Tests;

public sealed class FileOperationsTests
{
    // ===== RemoveFileBuilder Tests =====

    [Fact]
    public void RemoveFileBuilder_SetsAllProperties()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.RemoveFile(rf => rf
                .Id("RF001")
                .Directory("INSTALLDIR")
                .FileName("*.log")
                .OnInstall()
                .OnUninstall());
        });

        Assert.Single(package.RemoveFiles);
        var rf = package.RemoveFiles[0];
        Assert.Equal("RF001", rf.Id);
        Assert.Equal("INSTALLDIR", rf.DirectoryRef);
        Assert.Equal("*.log", rf.FileName);
        Assert.True(rf.OnInstall);
        Assert.True(rf.OnUninstall);
    }

    [Fact]
    public void RemoveFileBuilder_OnInstallOnly()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.RemoveFile(rf => rf
                .Id("RF002")
                .Directory("INSTALLDIR")
                .FileName("temp.dat")
                .OnInstall());
        });

        var rf = package.RemoveFiles[0];
        Assert.True(rf.OnInstall);
        Assert.False(rf.OnUninstall);
    }

    [Fact]
    public void RemoveFileBuilder_OnUninstallOnly()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.RemoveFile(rf => rf
                .Id("RF003")
                .Directory("INSTALLDIR")
                .OnUninstall());
        });

        var rf = package.RemoveFiles[0];
        Assert.False(rf.OnInstall);
        Assert.True(rf.OnUninstall);
        Assert.Null(rf.FileName);
    }

    [Fact]
    public void RemoveFileBuilder_NullFileName_MeansRemoveFolder()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.RemoveFile(rf => rf
                .Id("RF004")
                .Directory("INSTALLDIR")
                .OnUninstall());
        });

        Assert.Null(package.RemoveFiles[0].FileName);
    }

    [Fact]
    public void PackageBuilder_MultipleRemoveFiles_AddsAll()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.RemoveFile(rf => rf.Id("RF1").Directory("DIR1").OnInstall());
            p.RemoveFile(rf => rf.Id("RF2").Directory("DIR2").OnUninstall());
        });

        Assert.Equal(2, package.RemoveFiles.Count);
        Assert.Equal("RF1", package.RemoveFiles[0].Id);
        Assert.Equal("RF2", package.RemoveFiles[1].Id);
    }

    // ===== CreateFolderBuilder Tests =====

    [Fact]
    public void CreateFolderBuilder_SetsAllProperties()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CreateFolder(cf => cf
                .Id("CF001")
                .Directory("INSTALLDIR"));
        });

        Assert.Single(package.CreateFolders);
        var cf = package.CreateFolders[0];
        Assert.Equal("CF001", cf.Id);
        Assert.Equal("INSTALLDIR", cf.DirectoryRef);
    }

    [Fact]
    public void CreateFolderBuilder_DefaultComponentRefIsNull()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CreateFolder(cf => cf.Id("CF002").Directory("DATADIR"));
        });

        Assert.Null(package.CreateFolders[0].ComponentRef);
    }

    [Fact]
    public void PackageBuilder_MultipleCreateFolders_AddsAll()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CreateFolder(cf => cf.Id("CF1").Directory("DIR1"));
            p.CreateFolder(cf => cf.Id("CF2").Directory("DIR2"));
            p.CreateFolder(cf => cf.Id("CF3").Directory("DIR3"));
        });

        Assert.Equal(3, package.CreateFolders.Count);
    }

    // ===== MoveFileBuilder Tests =====

    [Fact]
    public void MoveFileBuilder_SetsAllProperties()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MoveFile(mf => mf
                .Id("MF001")
                .SourceDirectory("SOURCEDIR")
                .SourceFileName("*.txt")
                .DestDirectory("DESTDIR")
                .DestFileName("readme.txt")
                .AsMove());
        });

        Assert.Single(package.MoveFiles);
        var mf = package.MoveFiles[0];
        Assert.Equal("MF001", mf.Id);
        Assert.Equal("SOURCEDIR", mf.SourceDirectory);
        Assert.Equal("*.txt", mf.SourceFileName);
        Assert.Equal("DESTDIR", mf.DestDirectory);
        Assert.Equal("readme.txt", mf.DestFileName);
        Assert.Equal(1, mf.Options);
    }

    [Fact]
    public void MoveFileBuilder_DefaultOptions_IsMove()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MoveFile(mf => mf
                .Id("MF002")
                .SourceDirectory("SRC")
                .SourceFileName("data.bin")
                .DestDirectory("DST"));
        });

        Assert.Equal(1, package.MoveFiles[0].Options);
    }

    [Fact]
    public void MoveFileBuilder_AsCopy_SetsOptionsToZero()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MoveFile(mf => mf
                .Id("MF003")
                .SourceDirectory("SRC")
                .SourceFileName("data.bin")
                .DestDirectory("DST")
                .AsCopy());
        });

        Assert.Equal(0, package.MoveFiles[0].Options);
    }

    [Fact]
    public void MoveFileBuilder_AsMove_SetsOptionsToOne()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MoveFile(mf => mf
                .Id("MF004")
                .SourceDirectory("SRC")
                .SourceFileName("data.bin")
                .DestDirectory("DST")
                .AsCopy()
                .AsMove());
        });

        Assert.Equal(1, package.MoveFiles[0].Options);
    }

    [Fact]
    public void MoveFileBuilder_DestFileName_IsOptional()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MoveFile(mf => mf
                .Id("MF005")
                .SourceDirectory("SRC")
                .SourceFileName("*.log")
                .DestDirectory("DST"));
        });

        Assert.Null(package.MoveFiles[0].DestFileName);
    }

    [Fact]
    public void PackageBuilder_MultipleMoveFiles_AddsAll()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MoveFile(mf => mf.Id("MF1").SourceDirectory("S1").SourceFileName("a.txt").DestDirectory("D1"));
            p.MoveFile(mf => mf.Id("MF2").SourceDirectory("S2").SourceFileName("b.txt").DestDirectory("D2").AsCopy());
        });

        Assert.Equal(2, package.MoveFiles.Count);
        Assert.Equal(1, package.MoveFiles[0].Options);
        Assert.Equal(0, package.MoveFiles[1].Options);
    }

    // ===== DuplicateFileBuilder Tests =====

    [Fact]
    public void DuplicateFileBuilder_SetsAllProperties()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.DuplicateFile(df => df
                .Id("DF001")
                .FileRef("app.exe")
                .DestDirectory("BACKUPDIR")
                .DestFileName("app_backup.exe"));
        });

        Assert.Single(package.DuplicateFiles);
        var df = package.DuplicateFiles[0];
        Assert.Equal("DF001", df.Id);
        Assert.Equal("app.exe", df.FileRef);
        Assert.Equal("BACKUPDIR", df.DestDirectory);
        Assert.Equal("app_backup.exe", df.DestFileName);
    }

    [Fact]
    public void DuplicateFileBuilder_OptionalFields_AreNull()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.DuplicateFile(df => df
                .Id("DF002")
                .FileRef("config.json"));
        });

        var df = package.DuplicateFiles[0];
        Assert.Null(df.DestDirectory);
        Assert.Null(df.DestFileName);
    }

    [Fact]
    public void PackageBuilder_MultipleDuplicateFiles_AddsAll()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.DuplicateFile(df => df.Id("DF1").FileRef("a.exe"));
            p.DuplicateFile(df => df.Id("DF2").FileRef("b.dll").DestDirectory("BACKUP"));
        });

        Assert.Equal(2, package.DuplicateFiles.Count);
        Assert.Equal("DF1", package.DuplicateFiles[0].Id);
        Assert.Equal("DF2", package.DuplicateFiles[1].Id);
    }

    // ===== ComponentRef Tests =====

    [Fact]
    public void RemoveFileBuilder_ComponentRef_SetsProperty()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.RemoveFile(rf => rf
                .Id("RF_CR")
                .Directory("INSTALLDIR")
                .FileName("*.tmp")
                .OnUninstall()
                .ComponentRef("CustomComponent"));
        });

        Assert.Equal("CustomComponent", package.RemoveFiles[0].ComponentRef);
    }

    [Fact]
    public void RemoveFileBuilder_DefaultComponentRef_IsNull()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.RemoveFile(rf => rf
                .Id("RF_DEF")
                .Directory("INSTALLDIR")
                .OnUninstall());
        });

        Assert.Null(package.RemoveFiles[0].ComponentRef);
    }

    [Fact]
    public void CreateFolderBuilder_ComponentRef_SetsProperty()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.CreateFolder(cf => cf
                .Id("CF_CR")
                .Directory("DATADIR")
                .ComponentRef("DataComponent"));
        });

        Assert.Equal("DataComponent", package.CreateFolders[0].ComponentRef);
    }

    [Fact]
    public void MoveFileBuilder_ComponentRef_SetsProperty()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MoveFile(mf => mf
                .Id("MF_CR")
                .SourceDirectory("SRC")
                .SourceFileName("data.bin")
                .DestDirectory("DST")
                .ComponentRef("MoveComponent"));
        });

        Assert.Equal("MoveComponent", package.MoveFiles[0].ComponentRef);
    }

    [Fact]
    public void MoveFileBuilder_DefaultComponentRef_IsNull()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.MoveFile(mf => mf
                .Id("MF_DEF")
                .SourceDirectory("SRC")
                .SourceFileName("data.bin")
                .DestDirectory("DST"));
        });

        Assert.Null(package.MoveFiles[0].ComponentRef);
    }

    [Fact]
    public void DuplicateFileBuilder_ComponentRef_SetsProperty()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.DuplicateFile(df => df
                .Id("DF_CR")
                .FileRef("app.exe")
                .ComponentRef("DupComponent"));
        });

        Assert.Equal("DupComponent", package.DuplicateFiles[0].ComponentRef);
    }

    [Fact]
    public void DuplicateFileBuilder_DefaultComponentRef_IsNull()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.DuplicateFile(df => df
                .Id("DF_DEF")
                .FileRef("app.exe"));
        });

        Assert.Null(package.DuplicateFiles[0].ComponentRef);
    }

    // ===== Validation Tests =====

    [Fact]
    public void Validate_RemoveFile_MissingDirectoryRef_ProducesRMF001()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            RemoveFiles = [new RemoveFileModel { Id = "RF1", DirectoryRef = "", OnInstall = true }],
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "RMF001");
    }

    [Fact]
    public void Validate_RemoveFile_NeitherOnInstallNorOnUninstall_ProducesRMF002()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            RemoveFiles = [new RemoveFileModel { Id = "RF1", DirectoryRef = "INSTALLDIR", OnInstall = false, OnUninstall = false }],
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "RMF002");
    }

    [Fact]
    public void Validate_RemoveFile_Valid_NoErrors()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            RemoveFiles = [new RemoveFileModel { Id = "RF1", DirectoryRef = "INSTALLDIR", OnInstall = true }],
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = InstallerValidator.Validate(package);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_CreateFolder_MissingDirectoryRef_ProducesCRF001()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            CreateFolders = [new CreateFolderModel { Id = "CF1", DirectoryRef = "" }],
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "CRF001");
    }

    [Fact]
    public void Validate_CreateFolder_Valid_NoErrors()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            CreateFolders = [new CreateFolderModel { Id = "CF1", DirectoryRef = "DATADIR" }],
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = InstallerValidator.Validate(package);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_MoveFile_MissingSourceDirectory_ProducesMVF001()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            MoveFiles = [new MoveFileModel { Id = "MF1", SourceDirectory = "", SourceFileName = "*.txt", DestDirectory = "DST" }],
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "MVF001");
    }

    [Fact]
    public void Validate_MoveFile_MissingSourceFileName_ProducesMVF002()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            MoveFiles = [new MoveFileModel { Id = "MF1", SourceDirectory = "SRC", SourceFileName = "", DestDirectory = "DST" }],
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "MVF002");
    }

    [Fact]
    public void Validate_MoveFile_MissingDestDirectory_ProducesMVF003()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            MoveFiles = [new MoveFileModel { Id = "MF1", SourceDirectory = "SRC", SourceFileName = "*.txt", DestDirectory = "" }],
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "MVF003");
    }

    [Fact]
    public void Validate_MoveFile_Valid_NoErrors()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            MoveFiles = [new MoveFileModel { Id = "MF1", SourceDirectory = "SRC", SourceFileName = "*.txt", DestDirectory = "DST" }],
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = InstallerValidator.Validate(package);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_DuplicateFile_MissingFileRef_ProducesDPF001()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            DuplicateFiles = [new DuplicateFileModel { Id = "DF1", FileRef = "" }],
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "DPF001");
    }

    [Fact]
    public void Validate_DuplicateFile_Valid_NoErrors()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            DuplicateFiles = [new DuplicateFileModel { Id = "DF1", FileRef = "app.exe" }],
            Features = [new FeatureModel { Id = "Complete", Title = "Complete", IsRequired = true, IsDefault = true }]
        };

        var result = InstallerValidator.Validate(package);

        Assert.True(result.IsValid);
    }

    // ===== Integration Test =====

    [Fact]
    public void PackageBuilder_AllFileOperations_IntegrateCorrectly()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "IntegrationApp";
            p.Manufacturer = "IntegrationCorp";
            p.Version = new Version(2, 0, 0);
            p.RemoveFile(rf => rf.Id("RF1").Directory("INSTALLDIR").FileName("*.tmp").OnInstall());
            p.RemoveFile(rf => rf.Id("RF2").Directory("LOGDIR").OnUninstall());
            p.CreateFolder(cf => cf.Id("CF1").Directory("DATADIR"));
            p.MoveFile(mf => mf.Id("MF1").SourceDirectory("SRCDIR").SourceFileName("data.*").DestDirectory("DSTDIR").AsMove());
            p.MoveFile(mf => mf.Id("MF2").SourceDirectory("SRCDIR").SourceFileName("backup.*").DestDirectory("BKDIR").AsCopy());
            p.DuplicateFile(df => df.Id("DF1").FileRef("config.json").DestDirectory("BACKUPDIR").DestFileName("config.bak"));
        });

        Assert.Equal(2, package.RemoveFiles.Count);
        Assert.Single(package.CreateFolders);
        Assert.Equal(2, package.MoveFiles.Count);
        Assert.Single(package.DuplicateFiles);

        // Verify move vs copy options
        Assert.Equal(1, package.MoveFiles[0].Options);
        Assert.Equal(0, package.MoveFiles[1].Options);

        // Validate passes
        var validationResult = InstallerValidator.Validate(package);
        Assert.True(validationResult.IsValid);
    }
}
