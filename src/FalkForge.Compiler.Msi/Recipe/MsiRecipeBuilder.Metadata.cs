using FalkForge.Models;

namespace FalkForge.Compiler.Msi.Recipe;

/// <summary>
/// Derives the MSI PackageCode and builds the <see cref="SummaryInfoRecipe"/>
/// for the final recipe.
/// </summary>
public static partial class MsiRecipeBuilder
{
    // PID_REVNUMBER is the MSI PackageCode — must be unique per distinct package
    // byte sequence (SECREPAIR / KB2918614). Resolution order:
    //   1. Explicit PackageCode on the model (rare — pinned re-releases only).
    //   2. Reproducible mode → content digest via PackageCodeDerivation.Derive().
    //   3. Normal mode (null PackageCode) → derive from content + ResolvedPackage.InstanceId.
    //      InstanceId is a per-instance Guid assigned at ResolvedPackage construction,
    //      so two separate packaging events (different ResolvedPackage objects) produce
    //      different PackageCodes even with identical content, while multiple
    //      MsiRecipeBuilder.Build() calls on the *same* instance remain stable.
    private static Result<Guid> ResolvePackageCode(ResolvedPackage resolved)
    {
        if (resolved.Package.PackageCode.HasValue)
        {
            return Result<Guid>.Success(resolved.Package.PackageCode.Value);
        }

        var deriveResult = PackageCodeDerivation.Derive(resolved);
        if (deriveResult.IsFailure)
        {
            return Result<Guid>.Failure(deriveResult.Error);
        }

        return Result<Guid>.Success(deriveResult.Value);
    }

    private static SummaryInfoRecipe BuildSummaryInfo(PackageModel pkg, Guid packageCode)
    {
        return new SummaryInfoRecipe
        {
            Title = "Installation Database",
            Subject = pkg.Name,
            Author = pkg.Manufacturer,
            Keywords = "Installer",
            Comments = pkg.Description ??
                       $"This installer database contains the logic and data required to install {pkg.Name}.",
            Template = GetPlatformTemplate(pkg.Architecture),
            RevisionNumber = packageCode.ToString("B").ToUpperInvariant(),
            CodePage = 1252,
            CreatingApplication = "FalkForge",
            // WordCount 2 = compressed cabinet + long file-names support flag.
            WordCount = 2,
            // PageCount 200 = minimum required Windows Installer version (2.0).
            PageCount = 200,
            // Security 2 = read-only recommended (standard for shipped MSIs).
            Security = 2,
        };
    }
}
