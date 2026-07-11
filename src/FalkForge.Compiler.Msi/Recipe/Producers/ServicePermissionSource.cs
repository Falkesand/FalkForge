using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe.Producers;

/// <summary>
/// Enumerates every <see cref="PermissionModel"/> that should be projected into
/// the <c>LockPermissions</c> / <c>MsiLockPermissionsEx</c> tables: package-level
/// entries (<see cref="PackageModel.Permissions"/>) followed by each service's
/// own <see cref="ServiceModel.Permissions"/> (fluent <c>ServiceBuilder.Permission(...)</c>).
///
/// Package-level entries are yielded first, in their original order, so
/// <see cref="MsiLockPermissionsExTableProducer"/>'s legacy <c>PRM_{index:D4}</c>
/// numbering is unchanged for packages that only use package-level permissions
/// (byte-identical to the pre-existing behaviour). Per-service entries are
/// appended afterward in service-then-permission order.
///
/// <see cref="Builders.ServiceBuilder.Permission"/> stamps
/// <see cref="PermissionModel.LockObject"/> with the raw service name (verified
/// by <c>ServiceBuilderPermissionTests</c> at the Core model level) — that value
/// is not a valid MSI foreign key on its own, because the <c>ServiceInstall</c>
/// table's actual primary key is the synthesized identifier
/// <see cref="ServiceIdentity.ComputeServiceInstallId"/> produces, not the plain
/// service name. Producers must therefore always use this enumeration's
/// <c>EffectiveLockObject</c> rather than <c>Permission.LockObject</c> for
/// service-sourced entries.
/// </summary>
internal static class ServicePermissionSource
{
    internal static IEnumerable<(PermissionModel Permission, string EffectiveLockObject)> EnumerateAll(
        ResolvedPackage resolved)
    {
        foreach (PermissionModel permission in resolved.Package.Permissions)
        {
            yield return (permission, permission.LockObject);
        }

        foreach (ServiceModel service in resolved.Package.Services)
        {
            if (service.Permissions.Count == 0)
            {
                continue;
            }

            string serviceInstallId = ServiceIdentity.ComputeServiceInstallId(service.Name);
            foreach (PermissionModel permission in service.Permissions)
            {
                yield return (permission, serviceInstallId);
            }
        }
    }
}
