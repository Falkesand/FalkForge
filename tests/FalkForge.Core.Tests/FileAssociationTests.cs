using FalkForge.Builders;
using FalkForge.Models;
using FalkForge.Testing;
using FalkForge.Validation;
using Xunit;

namespace FalkForge.Core.Tests;

public sealed class FileAssociationTests
{
    [Fact]
    public void FileAssociationBuilder_SetsExtensionAndProgId()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.FileAssociation(".myext", fa =>
            {
                fa.ProgId("Corp.MyApp.Document");
                fa.Verb("open", "\"%1\"");
            });
        });

        Assert.Single(package.FileAssociations);
        Assert.Equal(".myext", package.FileAssociations[0].Extension);
        Assert.Equal("Corp.MyApp.Document", package.FileAssociations[0].ProgId);
    }

    [Fact]
    public void FileAssociationBuilder_SetsDescription()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.FileAssociation(".doc", fa =>
            {
                fa.ProgId("Corp.Doc");
                fa.Description = "My Document";
                fa.Verb("open", "\"%1\"");
            });
        });

        Assert.Equal("My Document", package.FileAssociations[0].Description);
    }

    [Fact]
    public void FileAssociationBuilder_SetsContentType()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.FileAssociation(".json", fa =>
            {
                fa.ProgId("Corp.JsonFile");
                fa.ContentType = "application/json";
                fa.Verb("open", "\"%1\"");
            });
        });

        Assert.Equal("application/json", package.FileAssociations[0].ContentType);
    }

    [Fact]
    public void FileAssociationBuilder_SetsIconProperties()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.FileAssociation(".myext", fa =>
            {
                fa.ProgId("Corp.MyExt");
                fa.IconFile = "app.ico";
                fa.IconIndex = 2;
                fa.Verb("open", "\"%1\"");
            });
        });

        Assert.Equal("app.ico", package.FileAssociations[0].IconFile);
        Assert.Equal(2, package.FileAssociations[0].IconIndex);
    }

    [Fact]
    public void FileAssociationBuilder_AddsMultipleVerbs()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.FileAssociation(".myext", fa =>
            {
                fa.ProgId("Corp.MyExt");
                fa.Verb("open", "\"%1\"");
                fa.Verb("edit", "\"%1\"", v => v.Sequence = 2);
                fa.Verb("print", "\"%1\"", v =>
                {
                    v.Sequence = 3;
                    v.Command = "&Print";
                });
            });
        });

        Assert.Equal(3, package.FileAssociations[0].Verbs.Count);
        Assert.Equal("open", package.FileAssociations[0].Verbs[0].Verb);
        Assert.Equal("edit", package.FileAssociations[0].Verbs[1].Verb);
        Assert.Equal("print", package.FileAssociations[0].Verbs[2].Verb);
        Assert.Equal("&Print", package.FileAssociations[0].Verbs[2].Command);
        Assert.Equal(3, package.FileAssociations[0].Verbs[2].Sequence);
    }

    [Fact]
    public void PackageBuilder_MultipleFileAssociations_AllAdded()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.FileAssociation(".ext1", fa =>
            {
                fa.ProgId("Corp.Ext1");
                fa.Verb("open", "\"%1\"");
            });
            p.FileAssociation(".ext2", fa =>
            {
                fa.ProgId("Corp.Ext2");
                fa.Verb("open", "\"%1\"");
            });
        });

        Assert.Equal(2, package.FileAssociations.Count);
        Assert.Equal(".ext1", package.FileAssociations[0].Extension);
        Assert.Equal(".ext2", package.FileAssociations[1].Extension);
    }

    [Fact]
    public void FileAssociation_DefaultVerbs_IsEmpty()
    {
        var model = new FileAssociationModel
        {
            Extension = ".test",
            ProgId = "Test.ProgId"
        };

        Assert.Empty(model.Verbs);
    }

    [Fact]
    public void Validate_FileAssociationWithEmptyExtension_ProducesFAS001()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            FileAssociations =
            [
                new FileAssociationModel
                {
                    Extension = "",
                    ProgId = "Corp.MyExt",
                    Verbs = [new VerbModel { Verb = "open" }]
                }
            ],
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

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.RuleId.Value == "FAS001");
    }

    [Fact]
    public void Validate_FileAssociationWithEmptyProgId_ProducesFAS002()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            FileAssociations =
            [
                new FileAssociationModel
                {
                    Extension = ".myext",
                    ProgId = "",
                    Verbs = [new VerbModel { Verb = "open" }]
                }
            ],
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

        var result = InstallerValidator.Validate(package);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.RuleId.Value == "FAS002");
    }

    [Fact]
    public void Validate_FileAssociationWithNoVerbs_ProducesFAS003Warning()
    {
        var package = new PackageModel
        {
            Name = "App",
            Manufacturer = "Corp",
            Version = new Version(1, 0, 0),
            UpgradeCode = Guid.NewGuid(),
            ProductCode = Guid.NewGuid(),
            FileAssociations =
            [
                new FileAssociationModel
                {
                    Extension = ".myext",
                    ProgId = "Corp.MyExt"
                }
            ],
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

        var result = InstallerValidator.Validate(package);

        Assert.Contains(result.Warnings, w => w.RuleId.Value == "FAS003");
    }

    [Fact]
    public void Validate_ValidFileAssociation_NoErrors()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.FileAssociation(".myext", fa =>
            {
                fa.ProgId("Corp.MyExt");
                fa.Verb("open", "\"%1\"");
            });
        });

        var result = InstallerValidator.Validate(package);

        Assert.DoesNotContain(result.Errors, e => e.RuleId.Value.StartsWith("FAS"));
    }

    [Fact]
    public void VerbBuilder_SetsAllProperties()
    {
        var package = InstallerTestHost.BuildPackage(p =>
        {
            p.Name = "App";
            p.Manufacturer = "Corp";
            p.FileAssociation(".myext", fa =>
            {
                fa.ProgId("Corp.MyExt");
                fa.Verb("open", "\"%1\"", v =>
                {
                    v.Command = "&Open";
                    v.Sequence = 1;
                });
            });
        });

        var verb = package.FileAssociations[0].Verbs[0];
        Assert.Equal("open", verb.Verb);
        Assert.Equal("\"%1\"", verb.Argument);
        Assert.Equal("&Open", verb.Command);
        Assert.Equal(1, verb.Sequence);
    }
}
