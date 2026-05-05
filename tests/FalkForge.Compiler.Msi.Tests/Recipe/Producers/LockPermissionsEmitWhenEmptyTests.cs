using System;
using System.Collections.Generic;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msi.Recipe;
using FalkForge.Compiler.Msi.Recipe.Producers;
using FalkForge.Extensibility;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Compiler.Msi.Tests.Recipe.Producers;

/// <summary>
/// Parity tests confirming that <c>LockPermissions</c> and
/// <c>MsiLockPermissionsEx</c> tables are suppressed from the recipe output
/// when the package has no permission entries — mirroring the legacy
/// <see cref="Tables.TableEmitter"/>, which only adds those CREATE TABLE
/// statements when at least one matching permission entry exists.
/// </summary>
public sealed class LockPermissionsEmitWhenEmptyTests
{
    // -------------------------------------------------------------------
    // Schema flag
    // -------------------------------------------------------------------

    [Fact]
    public void LockPermissionsTableProducer_schema_EmitWhenEmpty_is_false()
    {
        // The producer opts out of empty-table emission so the recipe builder
        // can suppress the table when zero rows are produced — parity with the
        // legacy conditional CREATE TABLE.
        LockPermissionsTableProducer producer = new();

        Assert.False(producer.Schema.EmitWhenEmpty);
    }

    [Fact]
    public void MsiLockPermissionsExTableProducer_schema_EmitWhenEmpty_is_false()
    {
        MsiLockPermissionsExTableProducer producer = new();

        Assert.False(producer.Schema.EmitWhenEmpty);
    }

    [Fact]
    public void RemoveIniFileTableProducer_schema_EmitWhenEmpty_is_true()
    {
        // RemoveIniFile IS always created by the legacy emitter — EmitWhenEmpty
        // must be true (or rely on the default) so the producer participates.
        RemoveIniFileTableProducer producer = new();

        Assert.True(producer.Schema.EmitWhenEmpty);
    }

    // -------------------------------------------------------------------
    // Recipe suppression — no permissions → Lock* tables absent
    // -------------------------------------------------------------------

    [Fact]
    public void MsiRecipeBuilder_with_no_permissions_does_not_emit_LockPermissions_table()
    {
        ResolvedPackage resolved = MakeResolved(permissions: Array.Empty<PermissionModel>());

        Result<MsiDatabaseRecipe> result = MsiRecipeBuilder.Build(
            resolved,
            new List<IMsiTableContributor>(),
            new MsiRecipeBuildOptions());

        Assert.True(result.IsSuccess);
        bool hasLockPerms = false;
        foreach (RecipeTable t in result.Value.Tables)
        {
            if (t.Name.Value == "LockPermissions")
            {
                hasLockPerms = true;
                break;
            }
        }

        Assert.False(hasLockPerms, "LockPermissions table must not appear when zero user-permission entries exist.");
    }

    [Fact]
    public void MsiRecipeBuilder_with_no_permissions_does_not_emit_MsiLockPermissionsEx_table()
    {
        ResolvedPackage resolved = MakeResolved(permissions: Array.Empty<PermissionModel>());

        Result<MsiDatabaseRecipe> result = MsiRecipeBuilder.Build(
            resolved,
            new List<IMsiTableContributor>(),
            new MsiRecipeBuildOptions());

        Assert.True(result.IsSuccess);
        bool hasMsiLockEx = false;
        foreach (RecipeTable t in result.Value.Tables)
        {
            if (t.Name.Value == "MsiLockPermissionsEx")
            {
                hasMsiLockEx = true;
                break;
            }
        }

        Assert.False(hasMsiLockEx, "MsiLockPermissionsEx table must not appear when zero SDDL-permission entries exist.");
    }

    [Fact]
    public void MsiRecipeBuilder_with_no_permissions_emits_thirty_five_tables()
    {
        // With both Lock* tables suppressed and RemoveIniFile added:
        // 36 (previous baseline) + 1 (RemoveIniFile) - 2 (Lock* suppressed) = 35.
        ResolvedPackage resolved = MakeResolved(permissions: Array.Empty<PermissionModel>());

        Result<MsiDatabaseRecipe> result = MsiRecipeBuilder.Build(
            resolved,
            new List<IMsiTableContributor>(),
            new MsiRecipeBuildOptions());

        Assert.True(result.IsSuccess);
        Assert.Equal(35, result.Value.Tables.Length);
    }

    // -------------------------------------------------------------------
    // Recipe inclusion — permissions present → Lock* tables appear
    // -------------------------------------------------------------------

    [Fact]
    public void MsiRecipeBuilder_with_user_permission_emits_LockPermissions_table()
    {
        // Domain must be non-null: LockPermissions uses a composite PK that
        // includes Domain, and PrimaryKeyValidator rejects null cells in PK
        // positions. The legacy MSI DDL marks Domain as nullable (the column
        // can be omitted at install time), but here we supply a value so the
        // recipe-level validator does not fail before we can check table presence.
        PermissionModel perm = new()
        {
            LockObject = "INSTALLDIR",
            Table = "CreateFolder",
            Domain = "BUILTIN",
            User = "Everyone",
            Permission = 0,
        };
        ResolvedPackage resolved = MakeResolved(permissions: new[] { perm });

        Result<MsiDatabaseRecipe> result = MsiRecipeBuilder.Build(
            resolved,
            new List<IMsiTableContributor>(),
            new MsiRecipeBuildOptions());

        Assert.True(result.IsSuccess);
        bool hasLockPerms = false;
        foreach (RecipeTable t in result.Value.Tables)
        {
            if (t.Name.Value == "LockPermissions")
            {
                hasLockPerms = true;
                break;
            }
        }

        Assert.True(hasLockPerms, "LockPermissions table must appear when at least one user-permission entry exists.");
    }

    [Fact]
    public void MsiRecipeBuilder_with_sddl_permission_emits_MsiLockPermissionsEx_table()
    {
        PermissionModel perm = new()
        {
            LockObject = "INSTALLDIR",
            Table = "CreateFolder",
            Sddl = "O:BAG:BAD:(A;;FA;;;BA)",
        };
        ResolvedPackage resolved = MakeResolved(permissions: new[] { perm });

        Result<MsiDatabaseRecipe> result = MsiRecipeBuilder.Build(
            resolved,
            new List<IMsiTableContributor>(),
            new MsiRecipeBuildOptions());

        Assert.True(result.IsSuccess);
        bool hasMsiLockEx = false;
        foreach (RecipeTable t in result.Value.Tables)
        {
            if (t.Name.Value == "MsiLockPermissionsEx")
            {
                hasMsiLockEx = true;
                break;
            }
        }

        Assert.True(hasMsiLockEx, "MsiLockPermissionsEx table must appear when at least one SDDL-permission entry exists.");
    }

    private static ResolvedPackage MakeResolved(IReadOnlyList<PermissionModel> permissions)
        => new()
        {
            Package = new PackageModel
            {
                Name = "T",
                Manufacturer = "M",
                Version = new Version(1, 0, 0),
                Permissions = permissions,
            },
            Components = Array.Empty<ResolvedComponent>(),
            Files = Array.Empty<ResolvedFile>(),
        };
}
