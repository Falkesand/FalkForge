using FalkForge.Cli;
using Xunit;

namespace FalkForge.Cli.Tests;

public sealed class JsonConfigLoaderTests
{
    private const string BaseDir = @"C:\test";

    [Fact]
    public void LoadFromString_MinimalValidJson_ReturnsSuccess()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            }
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.Equal("TestApp", result.Value.Name);
        Assert.Equal("TestCorp", result.Value.Manufacturer);
    }

    [Fact]
    public void LoadFromString_WithVersion_ParsesVersion()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp",
                "version": "2.1.0"
            }
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.Equal(new Version(2, 1, 0), result.Value.Version);
    }

    [Fact]
    public void LoadFromString_InvalidVersion_ReturnsJSN004()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp",
                "version": "not-a-version"
            }
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsFailure);
        Assert.Contains("JSN004", result.Error.Message);
    }

    [Fact]
    public void LoadFromString_MissingName_ReturnsJSN002()
    {
        var json = """
        {
            "product": {
                "manufacturer": "TestCorp"
            }
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsFailure);
        Assert.Contains("JSN002", result.Error.Message);
    }

    [Fact]
    public void LoadFromString_MissingManufacturer_ReturnsJSN003()
    {
        var json = """
        {
            "product": {
                "name": "TestApp"
            }
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsFailure);
        Assert.Contains("JSN003", result.Error.Message);
    }

    [Fact]
    public void LoadFromString_InvalidJson_ReturnsJSN001()
    {
        var json = "{ invalid json }}}";

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsFailure);
        Assert.Contains("JSN001", result.Error.Message);
    }

    [Fact]
    public void LoadFromString_ValidUpgradeCode_ParsesGuid()
    {
        var guid = Guid.NewGuid();
        var json = $$"""
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp",
                "upgradeCode": "{{guid}}"
            }
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.Equal(guid, result.Value.UpgradeCode);
    }

    [Fact]
    public void LoadFromString_InvalidUpgradeCode_ReturnsJSN005()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp",
                "upgradeCode": "not-a-guid"
            }
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsFailure);
        Assert.Contains("JSN005", result.Error.Message);
    }

    [Fact]
    public void LoadFromString_ValidDialogSet_SetsDialogSet()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "ui": "InstallDir"
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.Equal(FalkForge.Models.MsiDialogSet.InstallDir, result.Value.DialogSet);
    }

    [Fact]
    public void LoadFromString_InvalidDialogSet_ReturnsJSN006()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "ui": "NonExistent"
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsFailure);
        Assert.Contains("JSN006", result.Error.Message);
    }

    [Fact]
    public void LoadFromString_InvalidPlatform_ReturnsJSN007()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp",
                "platform": "MIPS"
            }
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsFailure);
        Assert.Contains("JSN007", result.Error.Message);
    }

    [Fact]
    public void LoadFromString_X64Platform_SetsArchitecture()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp",
                "platform": "X64"
            }
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.Equal(FalkForge.ProcessorArchitecture.X64, result.Value.Architecture);
    }

    [Fact]
    public void LoadFromString_WithDescription_SetsDescription()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp",
                "description": "A test application"
            }
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.Equal("A test application", result.Value.Description);
    }

    [Fact]
    public void LoadFromString_FeatureWithoutId_ReturnsJSN009()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "features": [
                {
                    "title": "No ID Feature"
                }
            ]
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsFailure);
        Assert.Contains("JSN009", result.Error.Message);
    }

    [Fact]
    public void LoadFromString_WithFeature_CreatesFeature()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "features": [
                {
                    "id": "MainFeature",
                    "title": "Main Feature",
                    "description": "The main feature",
                    "default": true,
                    "required": false
                }
            ]
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Features);
        Assert.Equal("MainFeature", result.Value.Features[0].Id);
        Assert.Equal("Main Feature", result.Value.Features[0].Title);
        Assert.Equal("The main feature", result.Value.Features[0].Description);
    }

    [Fact]
    public void LoadFromString_WithNestedFeatures_CreatesFeatureTree()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "features": [
                {
                    "id": "Parent",
                    "title": "Parent Feature",
                    "features": [
                        {
                            "id": "Child",
                            "title": "Child Feature"
                        }
                    ]
                }
            ]
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Features);
        var parent = result.Value.Features[0];
        Assert.Equal("Parent", parent.Id);
        Assert.Single(parent.Children);
        Assert.Equal("Child", parent.Children[0].Id);
    }

    [Fact]
    public void LoadFromString_WithMajorUpgrade_ConfiguresUpgrade()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "majorUpgrade": {
                "allowDowngrades": true,
                "downgradeMessage": "Cannot downgrade"
            }
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.MajorUpgrade);
    }

    [Fact]
    public void LoadFromString_WithLaunchConditions_AddsConditions()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "launchConditions": [
                {
                    "condition": "VersionNT >= 603",
                    "message": "Windows 8.1 or later required"
                }
            ]
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.LaunchConditions);
    }

    [Fact]
    public void LoadFromString_EmptyJson_ReturnsJSN002()
    {
        var json = "{}";

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsFailure);
        Assert.Contains("JSN002", result.Error.Message);
    }

    [Fact]
    public void LoadFromString_JsonWithComments_ParsesSuccessfully()
    {
        var json = """
        {
            // This is a comment
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            }
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void LoadFromString_JsonWithTrailingCommas_ParsesSuccessfully()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp",
            },
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void LoadFromString_CaseInsensitivePropertyNames_ParsesSuccessfully()
    {
        var json = """
        {
            "Product": {
                "Name": "TestApp",
                "Manufacturer": "TestCorp"
            }
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void LoadFromString_RequiredFeature_SetsIsRequired()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "features": [
                {
                    "id": "Core",
                    "title": "Core",
                    "required": true
                }
            ]
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Features[0].IsRequired);
    }

    [Fact]
    public void LoadFromString_NonDefaultFeature_SetsIsDefault()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "features": [
                {
                    "id": "Optional",
                    "title": "Optional Feature",
                    "default": false
                }
            ]
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Features[0].IsDefault);
    }

    [Fact]
    public void LoadFromString_MultipleFeatures_CreatesAll()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "features": [
                { "id": "Feature1", "title": "Feature 1" },
                { "id": "Feature2", "title": "Feature 2" },
                { "id": "Feature3", "title": "Feature 3" }
            ]
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Features.Count);
    }

    [Fact]
    public void LoadFromString_DefaultValues_AppliedCorrectly()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            }
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        var model = result.Value;
        Assert.Equal(new Version(1, 0, 0), model.Version);
        Assert.Equal(FalkForge.ProcessorArchitecture.X64, model.Architecture);
        Assert.NotEqual(Guid.Empty, model.UpgradeCode);
    }

    [Fact]
    public void LoadFromFile_NonExistentFile_ReturnsFailure()
    {
        var result = JsonConfigLoader.LoadFromFile(@"C:\nonexistent\installer.json");

        Assert.True(result.IsFailure);
        Assert.Equal(FalkForge.ErrorKind.FileNotFound, result.Error.Kind);
    }

    [Fact]
    public void LoadFromString_Arm64Platform_SetsArchitecture()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp",
                "platform": "Arm64"
            }
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.Equal(FalkForge.ProcessorArchitecture.Arm64, result.Value.Architecture);
    }

    [Fact]
    public void LoadFromString_CaseInsensitivePlatform_ParsesSuccessfully()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp",
                "platform": "x64"
            }
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.Equal(FalkForge.ProcessorArchitecture.X64, result.Value.Architecture);
    }

    [Fact]
    public void LoadFromString_InstallDirectory_CreatesInstallPath()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "installDirectory": "TestCorp/TestApp"
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.DefaultInstallDirectory);
    }

    [Fact]
    public void LoadFromString_WithLicense_SetsLicenseFile()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "license": "License.rtf"
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.LicenseFile);
        Assert.Contains("License.rtf", result.Value.LicenseFile);
    }

    [Fact]
    public void LoadFromString_WithService_CreatesService()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "features": [
                {
                    "id": "Main",
                    "title": "Main",
                    "services": [
                        {
                            "name": "TestSvc",
                            "displayName": "Test Service",
                            "description": "A test service",
                            "executable": "[INSTALLDIR]svc.exe",
                            "startType": "Automatic",
                            "account": "LocalService"
                        }
                    ]
                }
            ]
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Services);
        Assert.Equal("TestSvc", result.Value.Services[0].Name);
        Assert.Equal("Test Service", result.Value.Services[0].DisplayName);
    }

    [Fact]
    public void LoadFromString_WithRegistry_CreatesRegistryEntries()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "features": [
                {
                    "id": "Main",
                    "title": "Main",
                    "registry": [
                        {
                            "root": "HKLM",
                            "key": "Software\\TestCorp\\TestApp",
                            "name": "Version",
                            "value": "1.0"
                        }
                    ]
                }
            ]
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value.RegistryEntries);
    }

    [Fact]
    public void LoadFromString_WithEnvironmentVariable_CreatesEnvVar()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "features": [
                {
                    "id": "Main",
                    "title": "Main",
                    "environmentVariables": [
                        {
                            "name": "TEST_HOME",
                            "value": "[INSTALLDIR]",
                            "action": "Set",
                            "system": true
                        }
                    ]
                }
            ]
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.EnvironmentVariables);
        Assert.Equal("TEST_HOME", result.Value.EnvironmentVariables[0].Name);
    }

    [Fact]
    public void LoadFromString_WithShortcut_CreatesShortcut()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "features": [
                {
                    "id": "Main",
                    "title": "Main",
                    "files": [
                        {
                            "source": "app.exe",
                            "shortcut": {
                                "name": "TestApp",
                                "location": "Desktop",
                                "description": "Launch TestApp"
                            }
                        }
                    ]
                }
            ]
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Shortcuts);
        Assert.Equal("TestApp", result.Value.Shortcuts[0].Name);
    }

    [Fact]
    public void LoadFromString_ThreeLevelNestedFeatures_CreatesFullTree()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "features": [
                {
                    "id": "Level1",
                    "title": "Level 1",
                    "features": [
                        {
                            "id": "Level2",
                            "title": "Level 2",
                            "features": [
                                {
                                    "id": "Level3",
                                    "title": "Level 3"
                                }
                            ]
                        }
                    ]
                }
            ]
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        var level1 = result.Value.Features[0];
        Assert.Equal("Level1", level1.Id);
        Assert.Single(level1.Children);
        var level2 = level1.Children[0];
        Assert.Equal("Level2", level2.Id);
        Assert.Single(level2.Children);
        var level3 = level2.Children[0];
        Assert.Equal("Level3", level3.Id);
    }

    [Theory]
    [InlineData("Minimal")]
    [InlineData("InstallDir")]
    [InlineData("FeatureTree")]
    [InlineData("Mondo")]
    [InlineData("Advanced")]
    public void LoadFromString_EachDialogSet_ParsesSuccessfully(string dialogSet)
    {
        var json = $$"""
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "ui": "{{dialogSet}}"
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
    }

    // A well-formed extension block validates, but JSON-authored extensions are not yet applied to
    // the compiled MSI. Rather than silently drop security-relevant config, LoadFromString must fail
    // loud with JSN019 (see JsonConfigLoader Extensions guard). These three tests previously asserted
    // success — that encoded the silent drop and is exactly the bug being fixed.

    [Fact]
    public void LoadFromString_WithExtensionsFirewall_ReturnsJSN019()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "features": [
                { "id": "Main", "title": "Main" }
            ],
            "extensions": {
                "firewall": [
                    {
                        "id": "HttpRule",
                        "name": "Allow HTTP",
                        "protocol": "Tcp",
                        "port": "80",
                        "direction": "Inbound",
                        "action": "Allow"
                    }
                ]
            }
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsFailure);
        Assert.Contains("JSN019", result.Error.Message);
        Assert.Contains("fluent API", result.Error.Message);
    }

    [Fact]
    public void LoadFromString_WithExtensionsSql_ReturnsJSN019()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "features": [
                { "id": "Main", "title": "Main" }
            ],
            "extensions": {
                "sql": [
                    {
                        "id": "AppDb",
                        "server": "localhost",
                        "database": "TestDb",
                        "createOnInstall": true
                    }
                ]
            }
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsFailure);
        Assert.Contains("JSN019", result.Error.Message);
    }

    [Fact]
    public void LoadFromString_WithExtensionsDotNet_ReturnsJSN019()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "features": [
                { "id": "Main", "title": "Main" }
            ],
            "extensions": {
                "dotnet": [
                    {
                        "runtimeType": "Runtime",
                        "platform": "X64",
                        "minimumVersion": "8.0.0",
                        "variableName": "DOTNET8_FOUND"
                    }
                ]
            }
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsFailure);
        Assert.Contains("JSN019", result.Error.Message);
    }

    [Fact]
    public void LoadFromString_WithoutExtensions_IsUnaffectedByJSN019()
    {
        // Negative case: a config with no extensions block must never trip the JSN019 guard.
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "features": [
                { "id": "Main", "title": "Main" }
            ]
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
    }

    // ── Whitespace-only values (kills IsNullOrWhiteSpace → != "" mutations) ──

    [Fact]
    public void LoadFromString_WhitespaceOnlyName_ReturnsJSN002()
    {
        var json = """
        {
            "product": {
                "name": "   ",
                "manufacturer": "TestCorp"
            }
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsFailure);
        Assert.Contains("JSN002", result.Error.Message);
    }

    [Fact]
    public void LoadFromString_WhitespaceOnlyManufacturer_ReturnsJSN003()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "\t"
            }
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsFailure);
        Assert.Contains("JSN003", result.Error.Message);
    }

    [Fact]
    public void LoadFromString_WhitespaceOnlyVersion_TreatedAsAbsent()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp",
                "version": "  "
            }
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.Equal(new Version(1, 0, 0), result.Value.Version);
    }

    [Fact]
    public void LoadFromString_WhitespaceOnlyUpgradeCode_TreatedAsAbsent()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp",
                "upgradeCode": "   "
            }
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value.UpgradeCode);
    }

    // ── Compound && → || mutations (launch conditions) ──

    [Fact]
    public void LoadFromString_LaunchConditionEmptyCondition_IsSkipped()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "launchConditions": [
                {
                    "condition": "",
                    "message": "Some requirement"
                }
            ]
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.LaunchConditions);
    }

    [Fact]
    public void LoadFromString_LaunchConditionEmptyMessage_IsSkipped()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "launchConditions": [
                {
                    "condition": "VersionNT >= 603",
                    "message": ""
                }
            ]
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.LaunchConditions);
    }

    // ── Path resolution (kills Path.IsPathRooted mutation) ──

    [Fact]
    public void LoadFromString_AbsoluteLicensePath_NotCombinedWithBaseDir()
    {
        var absolutePath = @"C:\licenses\License.rtf";
        var json = $$"""
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "license": "{{absolutePath.Replace("\\", "\\\\")}}"
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.Equal(absolutePath, result.Value.LicenseFile);
    }

    [Fact]
    public void LoadFromString_RelativeLicensePath_CombinedWithBaseDir()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "license": "License.rtf"
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.StartsWith(BaseDir, result.Value.LicenseFile);
        Assert.EndsWith("License.rtf", result.Value.LicenseFile);
    }

    // ── Extension validation && mutations ──

    [Fact]
    public void LoadFromString_FirewallMissingName_ReturnsJSN011()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "extensions": {
                "firewall": [
                    {
                        "id": "HttpRule",
                        "port": "80"
                    }
                ]
            }
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsFailure);
        Assert.Contains("JSN011", result.Error.Message);
    }

    [Fact]
    public void LoadFromString_FirewallMissingBothPortAndProgram_ReturnsJSN011()
    {
        var json = """
        {
            "product": {"name": "App", "manufacturer": "Corp"},
            "extensions": {
                "firewall": [{"id": "r1", "name": "Rule"}]
            }
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsFailure);
        Assert.Contains("JSN011", result.Error.Message);
    }

    [Fact]
    public void LoadFromString_FirewallWithPortOnly_PassesFieldValidationThenJSN019()
    {
        // Port-only is field-valid (must NOT trip JSN011 — kills the "port-only wrongly errors"
        // mutation). Because the firewall block is well-formed, the not-yet-applied guard then
        // fails loud with JSN019 rather than JSN011, proving field validation ran and passed.
        var json = """
        {
            "product": {"name": "App", "manufacturer": "Corp"},
            "extensions": {
                "firewall": [{"id": "r1", "name": "Rule", "port": "80"}]
            }
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsFailure);
        Assert.Contains("JSN019", result.Error.Message);
        Assert.DoesNotContain("JSN011", result.Error.Message);
    }

    [Fact]
    public void LoadFromString_SqlMissingServer_ReturnsJSN013()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "extensions": {
                "sql": [
                    {
                        "id": "AppDb",
                        "database": "TestDb"
                    }
                ]
            }
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsFailure);
        Assert.Contains("JSN013", result.Error.Message);
    }

    [Fact]
    public void LoadFromString_SqlMissingDatabase_ReturnsJSN013()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "extensions": {
                "sql": [
                    {
                        "id": "AppDb",
                        "server": "localhost"
                    }
                ]
            }
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsFailure);
        Assert.Contains("JSN013", result.Error.Message);
    }

    // ── Install directory whitespace (kills IsNullOrWhiteSpace → != "" mutation) ──

    [Fact]
    public void LoadFromString_WhitespaceOnlyInstallDirectory_DoesNotUseWhitespace()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "installDirectory": "   "
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        // Whitespace-only installDirectory is treated as absent;
        // the model receives the builder-computed default (ProgramFiles/Manufacturer/Name)
        // rather than a path containing only spaces.
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.DefaultInstallDirectory);
        Assert.DoesNotContain("   ", result.Value.DefaultInstallDirectory.ToString());
    }

    [Fact]
    public void LoadFromString_NonWhitespaceInstallDirectory_UsesProvidedValue()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "installDirectory": "UniqueCompanyName/UniqueAppName"
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        // The provided installDirectory should appear in the path
        Assert.True(result.IsSuccess);
        Assert.Contains("UniqueCompanyName", result.Value.DefaultInstallDirectory!.ToString());
        Assert.Contains("UniqueAppName", result.Value.DefaultInstallDirectory.ToString());
    }

    // ── Feature with zero files vs one file (kills Count > 0 mutations) ──

    [Fact]
    public void LoadFromString_FeatureWithEmptyFilesArray_NoFilesAdded()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "features": [
                {
                    "id": "Main",
                    "title": "Main",
                    "files": []
                }
            ]
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
    }

    // ── Shortcut conditions (kills && → || mutations on Name/Source checks) ──

    [Fact]
    public void LoadFromString_ShortcutMissingName_ShortcutNotAdded()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "features": [
                {
                    "id": "Main",
                    "title": "Main",
                    "files": [
                        {
                            "source": "app.exe",
                            "shortcut": {
                                "name": "",
                                "location": "Desktop"
                            }
                        }
                    ]
                }
            ]
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Shortcuts);
    }

    [Fact]
    public void LoadFromString_ShortcutMissingSource_ShortcutNotAdded()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "features": [
                {
                    "id": "Main",
                    "title": "Main",
                    "files": [
                        {
                            "source": "",
                            "shortcut": {
                                "name": "TestApp",
                                "location": "Desktop"
                            }
                        }
                    ]
                }
            ]
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Shortcuts);
    }

    [Fact]
    public void LoadFromString_ShortcutWithDescription_SetsDescription()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "features": [
                {
                    "id": "Main",
                    "title": "Main",
                    "files": [
                        {
                            "source": "app.exe",
                            "shortcut": {
                                "name": "TestApp",
                                "location": "Desktop",
                                "description": "Launch TestApp"
                            }
                        }
                    ]
                }
            ]
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Shortcuts);
        Assert.Equal("Launch TestApp", result.Value.Shortcuts[0].Description);
    }

    [Fact]
    public void LoadFromString_ShortcutOnStartMenu_SetsLocation()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "features": [
                {
                    "id": "Main",
                    "title": "Main",
                    "files": [
                        {
                            "source": "app.exe",
                            "shortcut": {
                                "name": "TestApp",
                                "location": "StartMenu"
                            }
                        }
                    ]
                }
            ]
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Shortcuts);
    }

    [Fact]
    public void LoadFromString_ShortcutOnStartup_SetsLocation()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "features": [
                {
                    "id": "Main",
                    "title": "Main",
                    "files": [
                        {
                            "source": "app.exe",
                            "shortcut": {
                                "name": "TestApp",
                                "location": "Startup"
                            }
                        }
                    ]
                }
            ]
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Shortcuts);
    }

    // ── Registry && mutations (missing Key or Name skips entry) ──

    [Fact]
    public void LoadFromString_RegistryMissingKey_EntryNotAdded()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "features": [
                {
                    "id": "Main",
                    "title": "Main",
                    "registry": [
                        {
                            "root": "HKLM",
                            "key": "",
                            "name": "Version",
                            "value": "1.0"
                        }
                    ]
                }
            ]
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.RegistryEntries);
    }

    [Fact]
    public void LoadFromString_RegistryMissingName_EntryNotAdded()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "features": [
                {
                    "id": "Main",
                    "title": "Main",
                    "registry": [
                        {
                            "root": "HKLM",
                            "key": "Software\\TestCorp\\TestApp",
                            "name": "",
                            "value": "1.0"
                        }
                    ]
                }
            ]
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.RegistryEntries);
    }

    // ── ParseRegistryRoot logical mutations (HKCU, HKCR, HKU) ──

    [Theory]
    [InlineData("HKCU")]
    [InlineData("CurrentUser")]
    public void LoadFromString_RegistryRootHKCU_ParsesSuccessfully(string root)
    {
        var json = $$"""
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "features": [
                {
                    "id": "Main",
                    "title": "Main",
                    "registry": [
                        {
                            "root": "{{root}}",
                            "key": "Software\\TestCorp",
                            "name": "Version",
                            "value": "1.0"
                        }
                    ]
                }
            ]
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value.RegistryEntries);
    }

    [Theory]
    [InlineData("HKCR")]
    [InlineData("ClassesRoot")]
    public void LoadFromString_RegistryRootHKCR_ParsesSuccessfully(string root)
    {
        var json = $$"""
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "features": [
                {
                    "id": "Main",
                    "title": "Main",
                    "registry": [
                        {
                            "root": "{{root}}",
                            "key": "Software\\TestCorp",
                            "name": "Version",
                            "value": "1.0"
                        }
                    ]
                }
            ]
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value.RegistryEntries);
    }

    [Theory]
    [InlineData("HKU")]
    [InlineData("Users")]
    public void LoadFromString_RegistryRootHKU_ParsesSuccessfully(string root)
    {
        var json = $$"""
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "features": [
                {
                    "id": "Main",
                    "title": "Main",
                    "registry": [
                        {
                            "root": "{{root}}",
                            "key": "Software\\TestCorp",
                            "name": "Version",
                            "value": "1.0"
                        }
                    ]
                }
            ]
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value.RegistryEntries);
    }

    [Fact]
    public void LoadFromString_RegistryRootUnknown_DefaultsToHKLM()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "features": [
                {
                    "id": "Main",
                    "title": "Main",
                    "registry": [
                        {
                            "root": "UNKNOWN",
                            "key": "Software\\TestCorp",
                            "name": "Version",
                            "value": "1.0"
                        }
                    ]
                }
            ]
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value.RegistryEntries);
    }

    // ── Environment variable && mutations (missing Name or Value skips entry) ──

    [Fact]
    public void LoadFromString_EnvVarMissingName_EntryNotAdded()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "features": [
                {
                    "id": "Main",
                    "title": "Main",
                    "environmentVariables": [
                        {
                            "name": "",
                            "value": "[INSTALLDIR]",
                            "action": "Set",
                            "system": false
                        }
                    ]
                }
            ]
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.EnvironmentVariables);
    }

    [Fact]
    public void LoadFromString_EnvVarMissingValue_EntryNotAdded()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "features": [
                {
                    "id": "Main",
                    "title": "Main",
                    "environmentVariables": [
                        {
                            "name": "TEST_HOME",
                            "value": "",
                            "action": "Set",
                            "system": true
                        }
                    ]
                }
            ]
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.EnvironmentVariables);
    }

    [Fact]
    public void LoadFromString_EnvVarSystemFalse_SetsIsSystemFalse()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "features": [
                {
                    "id": "Main",
                    "title": "Main",
                    "environmentVariables": [
                        {
                            "name": "TEST_HOME",
                            "value": "[INSTALLDIR]",
                            "action": "Set",
                            "system": false
                        }
                    ]
                }
            ]
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.EnvironmentVariables);
        Assert.False(result.Value.EnvironmentVariables[0].IsSystem);
    }

    // ── MajorUpgrade schedule (kills block removal and whitespace mutations) ──

    [Fact]
    public void LoadFromString_MajorUpgradeWithSchedule_ParsesSuccessfully()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "majorUpgrade": {
                "schedule": "afterInstallExecute"
            }
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.MajorUpgrade);
    }

    [Fact]
    public void LoadFromString_MajorUpgradeWithWhitespaceSchedule_SkipsSchedule()
    {
        var json = """
        {
            "product": {
                "name": "TestApp",
                "manufacturer": "TestCorp"
            },
            "majorUpgrade": {
                "schedule": "  "
            }
        }
        """;

        var result = JsonConfigLoader.LoadFromString(json, BaseDir);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.MajorUpgrade);
    }
}
