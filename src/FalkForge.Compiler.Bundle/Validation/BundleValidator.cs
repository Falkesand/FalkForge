namespace FalkForge.Compiler.Bundle.Validation;

using FalkForge.Engine.Protocol.Manifest;

#pragma warning disable CA1822 // Stateless service class; instance method for future extensibility
public sealed class BundleValidator
{
    public Result<Unit> Validate(BundleModel model)
    {
        // BDL001: Name is required
        if (string.IsNullOrWhiteSpace(model.Name))
            return Result<Unit>.Failure(ErrorKind.BundleError, "BDL001: Bundle name is required");

        // BDL002: Manufacturer is required
        if (string.IsNullOrWhiteSpace(model.Manufacturer))
            return Result<Unit>.Failure(ErrorKind.BundleError, "BDL002: Bundle manufacturer is required");

        // BDL008: BundleId must not be empty
        if (model.BundleId == Guid.Empty)
            return Result<Unit>.Failure(ErrorKind.BundleError, "BDL008: BundleId must not be empty GUID");

        // BDL009: UpgradeCode must not be empty
        if (model.UpgradeCode == Guid.Empty)
            return Result<Unit>.Failure(ErrorKind.BundleError, "BDL009: UpgradeCode must not be empty GUID");

        // BDL003: Version must be valid
        if (!Version.TryParse(model.Version, out _))
            return Result<Unit>.Failure(ErrorKind.BundleError, $"BDL003: Invalid version format: {model.Version}");

        // BDL004: At least one package required
        if (model.Packages.Count == 0)
            return Result<Unit>.Failure(ErrorKind.BundleError, "BDL004: Bundle must contain at least one package");

        // BDL005: Package IDs must be unique
        var duplicateIds = model.Packages
            .GroupBy(p => p.Id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        if (duplicateIds.Length > 0)
            return Result<Unit>.Failure(ErrorKind.BundleError,
                $"BDL005: Duplicate package IDs: {string.Join(", ", duplicateIds)}");

        // BDL006: Package container references must resolve to defined containers
        var containerIds = new HashSet<string>(model.Containers.Select(c => c.Id));
        var invalidRefs = model.Packages
            .Where(p => p.ContainerId is not null && !containerIds.Contains(p.ContainerId))
            .Select(p => $"Package '{p.Id}' references undefined container '{p.ContainerId}'")
            .ToArray();
        if (invalidRefs.Length > 0)
            return Result<Unit>.Failure(ErrorKind.BundleError, $"BDL006: {string.Join("; ", invalidRefs)}");

        // BDL007: Custom UI requires a project path
        if (model.UiConfig is { UiType: BundleUiType.Custom } uiConfig &&
            string.IsNullOrWhiteSpace(uiConfig.CustomUiProjectPath))
            return Result<Unit>.Failure(ErrorKind.BundleError,
                "BDL007: Custom UI requires a project path (CustomUiProjectPath)");

        // BDL010: Variable name is required
        foreach (var variable in model.Variables)
            if (string.IsNullOrWhiteSpace(variable.Name))
                return Result<Unit>.Failure(ErrorKind.BundleError, "BDL010: Variable name is required");

        // BDL011: Variable names must be unique
        var duplicateVarNames = model.Variables
            .GroupBy(v => v.Name)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        if (duplicateVarNames.Length > 0)
            return Result<Unit>.Failure(ErrorKind.BundleError,
                $"BDL011: Duplicate variable names: {string.Join(", ", duplicateVarNames)}");

        // BDL012: Default value must match variable type
        foreach (var variable in model.Variables)
        {
            if (variable.DefaultValue is null)
                continue;

            if (variable.Type == BundleVariableType.Numeric && !long.TryParse(variable.DefaultValue, out _))
                return Result<Unit>.Failure(ErrorKind.BundleError,
                    $"BDL012: Variable '{variable.Name}' has type Numeric but default value '{variable.DefaultValue}' is not a valid integer");

            if (variable.Type == BundleVariableType.Version && !Version.TryParse(variable.DefaultValue, out _))
                return Result<Unit>.Failure(ErrorKind.BundleError,
                    $"BDL012: Variable '{variable.Name}' has type Version but default value '{variable.DefaultValue}' is not a valid version");
        }

        // BDL013: Secret variable cannot be Persisted
        foreach (var variable in model.Variables)
            if (variable.Secret && variable.Persisted)
                return Result<Unit>.Failure(ErrorKind.BundleError,
                    $"BDL013: Variable '{variable.Name}' is Secret and cannot be Persisted");

        // BDL014: Feature ID is required
        foreach (var feature in model.Features)
            if (string.IsNullOrWhiteSpace(feature.Id))
                return Result<Unit>.Failure(ErrorKind.BundleError, "BDL014: Feature ID is required");

        // BDL018: Feature title is required
        foreach (var feature in model.Features)
            if (string.IsNullOrWhiteSpace(feature.Title))
                return Result<Unit>.Failure(ErrorKind.BundleError, $"BDL018: Feature '{feature.Id}' title is required");

        // BDL015: Feature IDs must be unique
        var duplicateFeatureIds = model.Features
            .GroupBy(f => f.Id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        if (duplicateFeatureIds.Length > 0)
            return Result<Unit>.Failure(ErrorKind.BundleError,
                $"BDL015: Duplicate feature IDs: {string.Join(", ", duplicateFeatureIds)}");

        // BDL016: Feature package references must resolve to defined packages
        var packageIds = new HashSet<string>(
            model.Chain
                .OfType<PackageChainItem>()
                .Select(ci => ci.Package.Id));

        foreach (var feature in model.Features)
        {
            var unknownPackages = feature.PackageIds
                .Where(pid => !packageIds.Contains(pid))
                .ToArray();

            if (unknownPackages.Length > 0)
                return Result<Unit>.Failure(ErrorKind.BundleError,
                    $"BDL016: Feature '{feature.Id}' references unknown package IDs: {string.Join(", ", unknownPackages)}");
        }

        // BDL017: Required feature must have at least one package
        foreach (var feature in model.Features)
            if (feature.IsRequired && feature.PackageIds.Count == 0)
                return Result<Unit>.Failure(ErrorKind.BundleError,
                    $"BDL017: Required feature '{feature.Id}' has no packages");

        // BDL019: Dependency provider key must not be empty
        foreach (var provider in model.DependencyProviders)
        {
            if (string.IsNullOrWhiteSpace(provider.Key))
                return Result<Unit>.Failure(ErrorKind.BundleError,
                    "BDL019: Dependency provider key must not be empty.");

            // BDL020: Dependency provider version must be valid
            if (!string.IsNullOrWhiteSpace(provider.Version) && !Version.TryParse(provider.Version, out _))
                return Result<Unit>.Failure(ErrorKind.BundleError,
                    $"BDL020: Dependency provider '{provider.Key}' has invalid version '{provider.Version}'.");
        }

        // BDL021: Duplicate dependency provider keys
        var duplicateProviderKeys = model.DependencyProviders
            .Where(p => !string.IsNullOrWhiteSpace(p.Key))
            .GroupBy(p => p.Key)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        if (duplicateProviderKeys.Length > 0)
            return Result<Unit>.Failure(ErrorKind.BundleError,
                $"BDL021: Duplicate dependency provider key '{string.Join(", ", duplicateProviderKeys)}'.");

        // BDL022/BDL023: Dependency consumer validation
        foreach (var consumer in model.DependencyConsumers)
        {
            if (string.IsNullOrWhiteSpace(consumer.ProviderKey))
                return Result<Unit>.Failure(ErrorKind.BundleError,
                    "BDL022: Dependency consumer provider key must not be empty.");
            if (string.IsNullOrWhiteSpace(consumer.ConsumerKey))
                return Result<Unit>.Failure(ErrorKind.BundleError,
                    "BDL023: Dependency consumer key must not be empty.");
        }

        // BDL024/BDL025: Update feed validation
        if (model.UpdateFeed is not null)
        {
            if (!Uri.TryCreate(model.UpdateFeed.FeedUrl, UriKind.Absolute, out var feedUri))
                return Result<Unit>.Failure(ErrorKind.BundleError,
                    $"BDL024: Update feed URL '{model.UpdateFeed.FeedUrl}' is not a valid absolute URI.");
            if (feedUri.Scheme != Uri.UriSchemeHttps)
                return Result<Unit>.Failure(ErrorKind.BundleError,
                    $"BDL025: Update feed URL must use HTTPS scheme, got '{feedUri.Scheme}'.");

            // BDL031: Pinned publisher thumbprint must be a 40-character SHA-1 hex string.
            // An invalid thumbprint would silently never match a real certificate, turning
            // the security pin into an always-fail gate that blocks every update.
            var thumbprint = model.UpdateFeed.PublisherThumbprint;
            if (thumbprint is not null && !IsValidSha1Thumbprint(thumbprint))
                return Result<Unit>.Failure(ErrorKind.BundleError,
                    $"BDL031: Update publisher thumbprint '{thumbprint}' is not a valid SHA-1 Authenticode " +
                    "thumbprint (expected 40 hexadecimal characters).");
        }

        // BDL032: A pinned publisher thumbprint is meaningless without an update feed — the
        // engine only enforces it on a downloaded update bundle. Pinning one without configuring
        // UpdateFeed is a silent misconfiguration (the author believes updates are publisher-locked
        // when no update path exists), so fail loud rather than drop the pin.
        if (model.UpdatePublisherThumbprint is not null && model.UpdateFeed is null)
            return Result<Unit>.Failure(ErrorKind.BundleError,
                "BDL032: An update publisher thumbprint was pinned via PinUpdatePublisher but no " +
                "update feed is configured. Call UpdateFeed(...) to configure an update feed, or " +
                "remove the PinUpdatePublisher pin.");

        // BDL027: EnableFeatureSelection only valid for MsiPackage
        foreach (var pkg in model.Packages)
        {
            if (pkg.EnableFeatureSelection && pkg.Type != BundlePackageType.MsiPackage)
                return Result<Unit>.Failure(ErrorKind.BundleError,
                    $"BDL027: EnableFeatureSelection is only valid for MsiPackage type, but package '{pkg.Id}' is {pkg.Type}.");

            // BDL033: A remote-payload publisher pin must be a 64-character SHA-256 hex string
            // (the SubjectPublicKeyInfo hash). A malformed pin would never match a real signer,
            // silently turning the security pin into an always-fail gate that blocks the payload —
            // fail loud at author time instead.
            var pin = pkg.RemotePayload?.CertificatePublicKey;
            if (pin is not null && !IsValidSha256Hex(pin))
                return Result<Unit>.Failure(ErrorKind.BundleError,
                    $"BDL033: Remote payload certificate public-key pin for package '{pkg.Id}' is not a valid " +
                    "SHA-256 public-key hash (expected 64 hexadecimal characters).");
        }

        // Pre-UI prerequisite validation
        foreach (var prereq in model.PreUIPackages)
        {
            // BDL028: Pre-UI prereq must have at least one SearchCondition.
            // Without one, the engine has no way to detect if it's already installed and would
            // re-run the installer on every launch.
            if (prereq.SearchConditions.Count == 0)
                return Result<Unit>.Failure(ErrorKind.BundleError,
                    $"BDL028: Pre-UI prerequisite '{prereq.Id}' must have at least one SearchCondition. " +
                    "Without a search condition the engine cannot detect if the prerequisite is already installed " +
                    "and would reinstall it on every launch.");

            // BDL029: Pre-UI prereq must have non-empty Arguments.
            // Silent install flags (/quiet /norestart) are mandatory for non-interactive bootstrap.
            if (string.IsNullOrWhiteSpace(prereq.Arguments))
                return Result<Unit>.Failure(ErrorKind.BundleError,
                    $"BDL029: Pre-UI prerequisite '{prereq.Id}' must have non-empty Arguments. " +
                    "Silent install flags (e.g., /quiet /norestart) are required for non-interactive bootstrap.");

            // BDL030: Pre-UI prereq must have an embedded payload (SourcePath) or a RemotePayload.
            // Both null means the engine would have nothing to run if the prereq is missing.
            var hasEmbeddedSource = prereq.PayloadMode == PreUIPayloadMode.Embedded
                                    && !string.IsNullOrWhiteSpace(prereq.SourcePath);
            var hasRemotePayload = prereq.PayloadMode == PreUIPayloadMode.Remote
                                   && prereq.RemotePayload is not null;
            if (!hasEmbeddedSource && !hasRemotePayload)
                return Result<Unit>.Failure(ErrorKind.BundleError,
                    $"BDL030: Pre-UI prerequisite '{prereq.Id}' must specify either an embedded payload " +
                    "(non-empty SourcePath with PayloadMode=Embedded) or a RemotePayload " +
                    "(non-null RemotePayload with PayloadMode=Remote).");
        }

        return Unit.Value;
    }

    /// <summary>
    /// Validates that a string is a well-formed SHA-1 Authenticode thumbprint:
    /// exactly 40 hexadecimal characters. Avoids regex/LINQ to keep the hot validation
    /// path allocation-free (Gate 6).
    /// </summary>
    private static bool IsValidSha1Thumbprint(string thumbprint)
    {
        const int Sha1HexLength = 40;
        if (thumbprint.Length != Sha1HexLength)
            return false;

        foreach (var c in thumbprint)
        {
            var isHex = c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');
            if (!isHex)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Validates that a string is a well-formed SHA-256 public-key pin: exactly 64 hexadecimal
    /// characters. Avoids regex/LINQ to keep the validation path allocation-free (Gate 6).
    /// </summary>
    private static bool IsValidSha256Hex(string value)
    {
        const int Sha256HexLength = 64;
        if (value.Length != Sha256HexLength)
            return false;

        foreach (var c in value)
        {
            var isHex = c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');
            if (!isHex)
                return false;
        }

        return true;
    }
}