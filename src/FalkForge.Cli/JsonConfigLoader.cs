using System.Runtime.Versioning;
using System.Text.Json;
using FalkForge;
using FalkForge.Builders;
using FalkForge.Cli.Models;
using FalkForge.Extensibility;
using FalkForge.Extensions.Firewall;
using FalkForge.Extensions.Iis;
using FalkForge.Extensions.Iis.Models;
using FalkForge.Extensions.Sql.Builders;
using FalkForge.Models;

namespace FalkForge.Cli;

public static class JsonConfigLoader
{
    public static Result<PackageModel> LoadFromFile(string jsonPath)
    {
        if (!File.Exists(jsonPath))
            return Result<PackageModel>.Failure(new Error(ErrorKind.FileNotFound, $"JSON file not found: {jsonPath}"));

        var json = File.ReadAllText(jsonPath);
        var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(jsonPath)) ?? Environment.CurrentDirectory;
        return LoadFromString(json, baseDirectory);
    }

    public static Result<PackageModel> LoadFromString(string json, string baseDirectory)
    {
        InstallerConfig config;
        try
        {
            config = JsonSerializer.Deserialize(json, InstallerConfigJsonContext.Default.InstallerConfig)
                ?? new InstallerConfig();
        }
        catch (JsonException ex)
        {
            return Result<PackageModel>.Failure(new Error(ErrorKind.InvalidConfiguration, $"JSN001: Invalid JSON: {ex.Message}"));
        }

        return BuildPackageModel(config, baseDirectory);
    }

    /// <summary>
    /// Loads the optional <c>extensions</c> section of a forge JSON config and translates each
    /// present block (firewall / IIS / SQL) into the corresponding real
    /// <see cref="IFalkForgeExtension"/> instance — the SAME types the C# fluent API attaches via
    /// <c>new MsiCompiler().Use(extension)</c>. <see cref="Commands.BuildCommand"/> attaches the
    /// returned extensions to the compiler so a JSON-authored firewall rule / IIS site / SQL script
    /// is emitted into the compiled MSI. Loaded separately from <see cref="LoadFromFile"/> (which
    /// returns only the <see cref="PackageModel"/>), mirroring <see cref="LoadSigningFromFile"/>.
    /// An absent extensions section, or one whose blocks are all empty, returns an empty list.
    /// A <c>dotnet</c> block fails loud (JSN019): .NET runtime detection is a bundle-engine feature
    /// with no standalone-MSI representation.
    /// </summary>
    public static Result<IReadOnlyList<IFalkForgeExtension>> LoadExtensionsFromFile(string jsonPath)
    {
        if (!File.Exists(jsonPath))
            return Result<IReadOnlyList<IFalkForgeExtension>>.Failure(new Error(ErrorKind.FileNotFound, $"JSON file not found: {jsonPath}"));

        return LoadExtensionsFromString(File.ReadAllText(jsonPath));
    }

    /// <summary>String-input counterpart of <see cref="LoadExtensionsFromFile"/> (see there).</summary>
    public static Result<IReadOnlyList<IFalkForgeExtension>> LoadExtensionsFromString(string json)
    {
        InstallerConfig config;
        try
        {
            config = JsonSerializer.Deserialize(json, InstallerConfigJsonContext.Default.InstallerConfig)
                ?? new InstallerConfig();
        }
        catch (JsonException ex)
        {
            return Result<IReadOnlyList<IFalkForgeExtension>>.Failure(new Error(ErrorKind.InvalidConfiguration, $"JSN001: Invalid JSON: {ex.Message}"));
        }

        if (config.Extensions is null || !HasAnyExtensionContent(config.Extensions))
            return Result<IReadOnlyList<IFalkForgeExtension>>.Success([]);

        // Field-level validation (JSN011–JSN014) fires FIRST so a malformed block reports the precise
        // missing field before any extension instance is constructed.
        var validation = ValidateExtensions(config.Extensions);
        if (validation.IsFailure)
            return Result<IReadOnlyList<IFalkForgeExtension>>.Failure(validation.Error);

        return BuildExtensions(config.Extensions);
    }

    /// <summary>
    /// Translates a validated <see cref="ExtensionsConfig"/> into real extension instances. Reuses the
    /// extensions' own fluent builders (never re-implements their MSI emission), so the JSON path and the
    /// C# path produce identical tables. .NET detection is rejected up front (JSN019) rather than silently
    /// dropped, because attaching a detection-only extension to an MSI build gates on nothing.
    /// </summary>
    private static Result<IReadOnlyList<IFalkForgeExtension>> BuildExtensions(ExtensionsConfig extensions)
    {
        // .NET runtime detection is a bundle-engine feature: the DotNet extension contributes no MSI
        // tables (its Register is empty) and the runtime variable it names is populated by the engine's
        // detect phase, not by the MSI. Emitting it into a standalone MSI would produce an installer that
        // does NOT actually gate on the runtime — a silent security-relevant drop — so we fail loud.
        if (extensions.DotNet is { Count: > 0 })
            return Result<IReadOnlyList<IFalkForgeExtension>>.Failure(new Error(ErrorKind.Validation,
                "JSN019: JSON '.NET runtime detection' authoring is not supported — .NET detection is a " +
                "bundle-engine feature with no standalone-MSI representation. Author it with the C# fluent " +
                "API instead (new DotNetExtension().SearchForRuntime()... combined with package.Require(...))."));

        var result = new List<IFalkForgeExtension>();

        if (extensions.Firewall is { Count: > 0 })
        {
            var firewall = BuildFirewall(extensions.Firewall);
            if (firewall.IsFailure)
                return Result<IReadOnlyList<IFalkForgeExtension>>.Failure(firewall.Error);
            result.Add(firewall.Value);
        }

        if (extensions.Iis is not null
            && ((extensions.Iis.AppPools is { Count: > 0 }) || (extensions.Iis.WebSites is { Count: > 0 })))
        {
            // IisExtension is Windows-annotated (Microsoft.Web.Administration). A JSON IIS build only
            // makes sense on Windows (MSI compilation requires it anyway); fail loud elsewhere rather
            // than silently drop the IIS configuration.
            if (!OperatingSystem.IsWindows())
                return Result<IReadOnlyList<IFalkForgeExtension>>.Failure(new Error(ErrorKind.Validation,
                    "JSN012: JSON IIS authoring requires Windows."));

            var iis = BuildIis(extensions.Iis);
            if (iis.IsFailure)
                return Result<IReadOnlyList<IFalkForgeExtension>>.Failure(iis.Error);
            result.Add(iis.Value);
        }

        if (extensions.Sql is { Count: > 0 })
        {
            var sql = BuildSql(extensions.Sql);
            if (sql.IsFailure)
                return Result<IReadOnlyList<IFalkForgeExtension>>.Failure(sql.Error);
            result.Add(sql.Value);
        }

        return Result<IReadOnlyList<IFalkForgeExtension>>.Success(result);
    }

    private static Result<FirewallExtension> BuildFirewall(List<FirewallRuleConfig> rules)
    {
        var extension = new FirewallExtension();

        foreach (var rule in rules)
        {
            // Id/Name presence and the port-or-program requirement are guaranteed by ValidateExtensions
            // (JSN011), which ran before BuildExtensions.
            FirewallProtocol? protocol = null;
            if (!string.IsNullOrWhiteSpace(rule.Protocol))
            {
                if (!TryParseEnum(rule.Protocol, out FirewallProtocol parsed))
                    return FirewallEnumFailure(rule.Id!, "protocol", rule.Protocol, Enum.GetNames<FirewallProtocol>());
                protocol = parsed;
            }

            FirewallDirection? direction = null;
            if (!string.IsNullOrWhiteSpace(rule.Direction))
            {
                if (!TryParseEnum(rule.Direction, out FirewallDirection parsed))
                    return FirewallEnumFailure(rule.Id!, "direction", rule.Direction, Enum.GetNames<FirewallDirection>());
                direction = parsed;
            }

            FirewallRuleAction? action = null;
            if (!string.IsNullOrWhiteSpace(rule.Action))
            {
                if (!TryParseEnum(rule.Action, out FirewallRuleAction parsed))
                    return FirewallEnumFailure(rule.Id!, "action", rule.Action, Enum.GetNames<FirewallRuleAction>());
                action = parsed;
            }

            FirewallProfile? profile = null;
            if (!string.IsNullOrWhiteSpace(rule.Profile))
            {
                if (!TryParseEnum(rule.Profile, out FirewallProfile parsed))
                    return FirewallEnumFailure(rule.Id!, "profile", rule.Profile, Enum.GetNames<FirewallProfile>());
                profile = parsed;
            }

            extension.AddRule(b =>
            {
                b.Id(rule.Id!).Name(rule.Name!);
                if (!string.IsNullOrWhiteSpace(rule.Port))
                    b.Port(rule.Port);
                if (!string.IsNullOrWhiteSpace(rule.Program))
                    b.Program(rule.Program);
                if (protocol is not null)
                    b.Protocol(protocol.Value);
                if (direction is not null)
                    b.Direction(direction.Value);
                if (action is not null)
                    b.Action(action.Value);
                if (profile is not null)
                    b.Profile(profile.Value);
            });
        }

        return Result<FirewallExtension>.Success(extension);
    }

    [SupportedOSPlatform("windows")]
    private static Result<IisExtension> BuildIis(IisConfig config)
    {
        var extension = new IisExtension();

        if (config.AppPools is not null)
        {
            foreach (var pool in config.AppPools)
            {
                // Name presence is guaranteed by ValidateExtensions (JSN012).
                ManagedPipelineMode? pipelineMode = null;
                if (!string.IsNullOrWhiteSpace(pool.PipelineMode))
                {
                    if (!TryParseEnum(pool.PipelineMode, out ManagedPipelineMode parsed))
                        return IisEnumFailure("app pool", pool.Name!, "pipelineMode", pool.PipelineMode, Enum.GetNames<ManagedPipelineMode>());
                    pipelineMode = parsed;
                }

                AppPoolIdentityType? identity = null;
                if (!string.IsNullOrWhiteSpace(pool.Identity))
                {
                    if (!TryParseEnum(pool.Identity, out AppPoolIdentityType parsed))
                        return IisEnumFailure("app pool", pool.Name!, "identity", pool.Identity, Enum.GetNames<AppPoolIdentityType>());
                    identity = parsed;
                }

                extension.AddAppPool(b =>
                {
                    if (!string.IsNullOrWhiteSpace(pool.Id))
                        b.Id(pool.Id);
                    b.Name(pool.Name!);
                    if (!string.IsNullOrWhiteSpace(pool.RuntimeVersion))
                        b.Runtime(pool.RuntimeVersion);
                    if (pipelineMode is not null)
                        b.PipelineMode(pipelineMode.Value);
                    if (identity is not null)
                        b.Identity(identity.Value);
                });
            }
        }

        if (config.WebSites is not null)
        {
            foreach (var site in config.WebSites)
            {
                // Description and at least one binding are guaranteed by ValidateExtensions (JSN012).
                extension.AddWebSite(b =>
                {
                    if (!string.IsNullOrWhiteSpace(site.Id))
                        b.Id(site.Id);
                    b.Description(site.Description!);
                    if (!string.IsNullOrWhiteSpace(site.Directory))
                        b.Directory(site.Directory);
                    if (!string.IsNullOrWhiteSpace(site.AppPool))
                        b.AppPool(site.AppPool);
                    foreach (var binding in site.Bindings!)
                        b.Binding(binding.Port, string.IsNullOrWhiteSpace(binding.Protocol) ? "http" : binding.Protocol, binding.Host);
                });
            }
        }

        return Result<IisExtension>.Success(extension);
    }

    private static Result<Extensions.Sql.SqlExtension> BuildSql(List<SqlConfig> databases)
    {
        var extension = new Extensions.Sql.SqlExtension();

        for (var i = 0; i < databases.Count; i++)
        {
            var db = databases[i];
            // Server/Database presence is guaranteed by ValidateExtensions (JSN013). The SQL extension
            // additionally requires a database Id (SQL011); the JSON schema leaves it optional, so a
            // deterministic id is synthesised when absent.
            var databaseId = string.IsNullOrWhiteSpace(db.Id) ? $"Db{i}" : db.Id;

            var dbRef = extension.DefineDatabase(b => b
                .Id(databaseId)
                .Server(db.Server!)
                .Database(db.Database!)
                .CreateOnInstall(db.CreateOnInstall)
                .DropOnUninstall(db.DropOnUninstall));
            if (dbRef.IsFailure)
                return Result<Extensions.Sql.SqlExtension>.Failure(dbRef.Error);

            if (db.Scripts is null)
                continue;

            for (var j = 0; j < db.Scripts.Count; j++)
            {
                var script = db.Scripts[j];
                var scriptBuilder = new SqlScriptBuilder()
                    .Id(string.IsNullOrWhiteSpace(script.Id) ? $"{databaseId}_Script{j}" : script.Id)
                    .Database(dbRef.Value)
                    .ExecuteOnInstall(script.ExecuteOnInstall)
                    .ExecuteOnUninstall(script.ExecuteOnUninstall)
                    .Sequence(script.Sequence);
                if (!string.IsNullOrWhiteSpace(script.SourceFile))
                    scriptBuilder.SourceFile(script.SourceFile);

                var scriptModel = scriptBuilder.Build();
                if (scriptModel.IsFailure)
                    return Result<Extensions.Sql.SqlExtension>.Failure(scriptModel.Error);

                extension.Scripts.Add(scriptModel.Value);
            }
        }

        return Result<Extensions.Sql.SqlExtension>.Success(extension);
    }

    private static Result<FirewallExtension> FirewallEnumFailure(string id, string field, string value, string[] valid) =>
        Result<FirewallExtension>.Failure(new Error(ErrorKind.Validation,
            $"JSN011: Firewall rule '{id}' has invalid {field} '{value}'. Valid values: {string.Join(", ", valid)}"));

    [SupportedOSPlatform("windows")]
    private static Result<IisExtension> IisEnumFailure(string kind, string name, string field, string value, string[] valid) =>
        Result<IisExtension>.Failure(new Error(ErrorKind.Validation,
            $"JSN012: IIS {kind} '{name}' has invalid {field} '{value}'. Valid values: {string.Join(", ", valid)}"));

    private static bool TryParseEnum<T>(string value, out T parsed) where T : struct, Enum
    {
        if (Enum.TryParse(value, ignoreCase: true, out parsed))
        {
            // Flags enums (e.g. FirewallProfile) accept defined single values; non-flags reject any
            // token that parsed only as a raw numeric (Enum.TryParse succeeds for undefined numbers).
            if (typeof(T).IsDefined(typeof(FlagsAttribute), inherit: false) || Enum.IsDefined(parsed))
                return true;
        }

        parsed = default;
        return false;
    }

    /// <summary>
    /// Loads and validates the optional <c>signing</c> section of a forge JSON config
    /// (structural validation only — no environment access; build-time resolution of
    /// env-referenced material happens in <c>SigningProviderFactory</c>).
    /// An absent signing section normalizes to a config with provider <c>none</c>.
    /// </summary>
    public static Result<SigningConfig> LoadSigningFromFile(string jsonPath)
    {
        if (!File.Exists(jsonPath))
            return Result<SigningConfig>.Failure(new Error(ErrorKind.FileNotFound, $"JSON file not found: {jsonPath}"));

        return LoadSigningFromString(File.ReadAllText(jsonPath));
    }

    /// <summary>String-input counterpart of <see cref="LoadSigningFromFile"/> (see there).</summary>
    public static Result<SigningConfig> LoadSigningFromString(string json)
    {
        InstallerConfig config;
        try
        {
            config = JsonSerializer.Deserialize(json, InstallerConfigJsonContext.Default.InstallerConfig)
                ?? new InstallerConfig();
        }
        catch (JsonException ex)
        {
            return Result<SigningConfig>.Failure(new Error(ErrorKind.InvalidConfiguration, $"JSN001: Invalid JSON: {ex.Message}"));
        }

        return ValidateSigning(config.Signing);
    }

    private static Result<SigningConfig> ValidateSigning(SigningConfig? signing)
    {
        if (signing is null)
            return Result<SigningConfig>.Success(new SigningConfig { Provider = "none" });

        // The signing section is security-sensitive: unknown keys fail closed (a typo must not
        // silently disable authentication), and secret-looking keys get the explicit
        // "secrets come from the environment" guidance instead of being silently ignored.
        if (signing.UnknownFields is { Count: > 0 })
        {
            var name = signing.UnknownFields.Keys.First();
            return SigningFailure(LooksLikeSecretName(name)
                ? $"JSN016: signing.{name} is not a valid field — secrets must come from environment variables, not the config file. Reference the environment variable NAME via the matching '*Env' field (e.g. bearerTokenEnv)."
                : $"JSN016: Unknown field 'signing.{name}'. The signing section rejects unrecognized fields.");
        }

        if (string.IsNullOrWhiteSpace(signing.Provider))
            return SigningFailure("JSN015: signing.provider is required. Valid values: none, pem, signserver");

        if (Eq(signing.Provider, "none"))
            return Result<SigningConfig>.Success(signing);

        if (Eq(signing.Provider, "pem"))
            return ValidatePemSigning(signing);

        if (Eq(signing.Provider, "signserver"))
            return ValidateSignServerSigning(signing);

        return SigningFailure($"JSN015: Unknown signing provider '{signing.Provider}'. Valid values: none, pem, signserver");
    }

    private static Result<SigningConfig> ValidatePemSigning(SigningConfig signing)
    {
        var hasPath = !string.IsNullOrWhiteSpace(signing.KeyPath);
        var hasEnv = !string.IsNullOrWhiteSpace(signing.KeyEnv);

        if (hasPath == hasEnv) // neither, or ambiguously both
            return SigningFailure("JSN017: signing provider 'pem' requires exactly one key source: 'keyPath' (PEM file path) or 'keyEnv' (environment variable name holding the PEM).");

        if (hasPath && signing.KeyPath!.Contains("-----BEGIN", StringComparison.Ordinal))
            return SigningFailure("JSN016: signing.keyPath contains inline PEM key material — secrets must come from a key FILE or an environment variable, not the config file.");

        if (hasEnv && !IsValidEnvVarName(signing.KeyEnv!))
            return SigningFailure("JSN016: signing.keyEnv must be an environment variable NAME (letters, digits, underscore) — it looks like literal key material.");

        // Hybrid post-quantum companion (optional; present ⇒ hybrid). The PQ key follows the SAME
        // secret rules as the classical key: file path or env var NAME, never inline material.
        var hasPqPath = !string.IsNullOrWhiteSpace(signing.PqKeyPath);
        var hasPqEnv = !string.IsNullOrWhiteSpace(signing.PqKeyEnv);

        if (hasPqPath && hasPqEnv) // ambiguously both — refusing beats picking one silently
            return SigningFailure("JSN017: signing provider 'pem' accepts at most one post-quantum key source: 'pqKeyPath' (ML-DSA PEM file path) or 'pqKeyEnv' (environment variable name holding the PEM).");

        if (hasPqPath && signing.PqKeyPath!.Contains("-----BEGIN", StringComparison.Ordinal))
            return SigningFailure("JSN016: signing.pqKeyPath contains inline PEM key material — secrets must come from a key FILE or an environment variable, not the config file.");

        if (hasPqEnv && !IsValidEnvVarName(signing.PqKeyEnv!))
            return SigningFailure("JSN016: signing.pqKeyEnv must be an environment variable NAME (letters, digits, underscore) — it looks like literal key material.");

        return Result<SigningConfig>.Success(signing);
    }

    private static Result<SigningConfig> ValidateSignServerSigning(SigningConfig signing)
    {
        // SignServer ML-DSA workers are a Stage-4 assessment (PQ-hybrid design §8.6): until then
        // the PQ fields are pem-only. Failing loud beats silently emitting a classical-only bundle
        // the author believes is hybrid-signed.
        if (!string.IsNullOrWhiteSpace(signing.PqKeyPath) || !string.IsNullOrWhiteSpace(signing.PqKeyEnv))
            return SigningFailure("JSN018: signing provider 'signserver' does not support 'pqKeyPath'/'pqKeyEnv' — hybrid post-quantum signing currently requires provider 'pem'.");

        if (string.IsNullOrWhiteSpace(signing.BaseUrl))
            return SigningFailure("JSN018: signing provider 'signserver' requires 'baseUrl'.");

        if (string.IsNullOrWhiteSpace(signing.Worker))
            return SigningFailure("JSN018: signing provider 'signserver' requires 'worker'.");

        var authMode = signing.AuthMode;
        if (!string.IsNullOrWhiteSpace(authMode)
            && !Eq(authMode, "none") && !Eq(authMode, "basic") && !Eq(authMode, "bearer") && !Eq(authMode, "clientcert"))
        {
            return SigningFailure($"JSN018: Unknown signing.authMode '{authMode}'. Valid values: none, basic, bearer, clientcert");
        }

        // Fail closed: a chosen auth mode must name its environment source up front — there is
        // no fallback to an unauthenticated request.
        if (Eq(authMode, "bearer") && string.IsNullOrWhiteSpace(signing.BearerTokenEnv))
            return SigningFailure("JSN018: signing.authMode 'bearer' requires 'bearerTokenEnv' (environment variable name holding the token).");

        if (Eq(authMode, "basic")
            && (string.IsNullOrWhiteSpace(signing.UsernameEnv) || string.IsNullOrWhiteSpace(signing.PasswordEnv)))
        {
            return SigningFailure("JSN018: signing.authMode 'basic' requires 'usernameEnv' and 'passwordEnv' (environment variable names).");
        }

        if (Eq(authMode, "clientcert") && string.IsNullOrWhiteSpace(signing.ClientCertPathEnv))
            return SigningFailure("JSN018: signing.authMode 'clientcert' requires 'clientCertPathEnv' (environment variable name holding the PFX path).");

        foreach (var (field, value) in new[]
                 {
                     ("bearerTokenEnv", signing.BearerTokenEnv),
                     ("usernameEnv", signing.UsernameEnv),
                     ("passwordEnv", signing.PasswordEnv),
                     ("clientCertPathEnv", signing.ClientCertPathEnv),
                     ("clientCertPasswordEnv", signing.ClientCertPasswordEnv),
                 })
        {
            if (!string.IsNullOrWhiteSpace(value) && !IsValidEnvVarName(value))
                return SigningFailure($"JSN016: signing.{field} must be an environment variable NAME (letters, digits, underscore) — it looks like a literal credential. Secrets must come from the environment, not the config file.");
        }

        return Result<SigningConfig>.Success(signing);
    }

    private static Result<SigningConfig> SigningFailure(string message) =>
        Result<SigningConfig>.Failure(new Error(ErrorKind.Validation, message));

    private static bool Eq(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeSecretName(string name) =>
        name.Contains("token", StringComparison.OrdinalIgnoreCase)
        || name.Contains("password", StringComparison.OrdinalIgnoreCase)
        || name.Contains("secret", StringComparison.OrdinalIgnoreCase)
        || name.Contains("credential", StringComparison.OrdinalIgnoreCase)
        || name.Contains("passphrase", StringComparison.OrdinalIgnoreCase)
        || name.Contains("pem", StringComparison.OrdinalIgnoreCase)
        || name.Contains("key", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidEnvVarName(string value)
    {
        if (value.Length == 0 || (!char.IsAsciiLetter(value[0]) && value[0] != '_'))
            return false;

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
                return false;
        }

        return true;
    }

    private static Result<PackageModel> BuildPackageModel(InstallerConfig config, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(config.Product.Name))
            return Result<PackageModel>.Failure(new Error(ErrorKind.Validation, "JSN002: Missing required field: product.name"));

        if (string.IsNullOrWhiteSpace(config.Product.Manufacturer))
            return Result<PackageModel>.Failure(new Error(ErrorKind.Validation, "JSN003: Missing required field: product.manufacturer"));

        var builder = new PackageBuilder
        {
            Name = config.Product.Name,
            Manufacturer = config.Product.Manufacturer,
        };

        // Version
        if (!string.IsNullOrWhiteSpace(config.Product.Version))
        {
            if (!Version.TryParse(config.Product.Version, out var version))
                return Result<PackageModel>.Failure(new Error(ErrorKind.Validation, $"JSN004: Invalid version format: {config.Product.Version}"));
            builder.Version = version;
        }

        // UpgradeCode
        if (!string.IsNullOrWhiteSpace(config.Product.UpgradeCode))
        {
            if (!Guid.TryParse(config.Product.UpgradeCode, out var upgradeCode))
                return Result<PackageModel>.Failure(new Error(ErrorKind.Validation, $"JSN005: Invalid upgrade code GUID: {config.Product.UpgradeCode}"));
            builder.UpgradeCode = upgradeCode;
        }

        // Description
        if (!string.IsNullOrWhiteSpace(config.Product.Description))
            builder.Description = config.Product.Description;

        // Platform
        if (!string.IsNullOrWhiteSpace(config.Product.Platform))
        {
            if (!Enum.TryParse<ProcessorArchitecture>(config.Product.Platform, true, out var arch))
                return Result<PackageModel>.Failure(new Error(ErrorKind.Validation, $"JSN007: Invalid platform: {config.Product.Platform}. Valid values: X86, X64, Arm64"));
            builder.Architecture = arch;
        }

        // UI Dialog Set
        if (!string.IsNullOrWhiteSpace(config.Ui))
        {
            if (!Enum.TryParse<MsiDialogSet>(config.Ui, true, out var dialogSet))
                return Result<PackageModel>.Failure(new Error(ErrorKind.Validation, $"JSN006: Invalid UI dialog set: {config.Ui}. Valid values: None, Minimal, InstallDir, FeatureTree, Mondo, Advanced"));
            builder.UseDialogSet(dialogSet);
        }

        // License
        if (!string.IsNullOrWhiteSpace(config.License))
        {
            var licensePath = ResolvePath(config.License, baseDirectory);
            builder.LicenseFile = licensePath;
        }

        // Install Directory
        if (!string.IsNullOrWhiteSpace(config.InstallDirectory))
        {
            builder.DefaultInstallDirectory = KnownFolder.ProgramFiles / config.InstallDirectory;
        }

        // Major Upgrade
        if (config.MajorUpgrade is not null)
        {
            builder.MajorUpgrade(mu =>
            {
                if (!string.IsNullOrWhiteSpace(config.MajorUpgrade.Schedule))
                {
                    if (Enum.TryParse<RemoveExistingProductsSchedule>(config.MajorUpgrade.Schedule, true, out var schedule))
                        mu.Schedule(schedule);
                }
            });
        }

        // Downgrade
        if (config.Downgrade is not null)
        {
            builder.Downgrade(d =>
            {
                if (config.Downgrade.Allow)
                    d.Allow();
                else if (!string.IsNullOrWhiteSpace(config.Downgrade.Message))
                    d.Block(config.Downgrade.Message);
            });
        }

        // Launch Conditions
        if (config.LaunchConditions is not null)
        {
            foreach (var lc in config.LaunchConditions)
            {
                if (!string.IsNullOrWhiteSpace(lc.Condition) && !string.IsNullOrWhiteSpace(lc.Message))
                    builder.Require(lc.Condition, lc.Message);
            }
        }

        // Features
        if (config.Features is not null)
        {
            foreach (var featureConfig in config.Features)
            {
                if (string.IsNullOrWhiteSpace(featureConfig.Id))
                    return Result<PackageModel>.Failure(new Error(ErrorKind.Validation, "JSN009: Feature must have an id"));

                var featureResult = ConfigureFeature(builder, featureConfig, baseDirectory);
                if (featureResult.IsFailure)
                    return Result<PackageModel>.Failure(featureResult.Error);
            }
        }

        // Extensions: field-level structure is validated here (JSN011–JSN014) so a malformed block
        // fails on the model-load path too. The actual firewall/IIS/SQL extension INSTANCES are built
        // by LoadExtensions* (mirroring how the signing section is loaded separately) and attached to
        // the compiler by BuildCommand, so the JSON path emits the same MSI output the C# fluent API
        // produces. Only .NET runtime detection remains unsupported in JSON (see BuildExtensions).
        if (config.Extensions is not null)
        {
            var extensionResult = ValidateExtensions(config.Extensions);
            if (extensionResult.IsFailure)
                return Result<PackageModel>.Failure(extensionResult.Error);
        }

        try
        {
            var model = builder.Build();
            return Result<PackageModel>.Success(model);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return Result<PackageModel>.Failure(new Error(ErrorKind.InvalidConfiguration, $"JSN010: Configuration error: {ex.Message}"));
        }
    }

    private static Result<Unit> ConfigureFeature(PackageBuilder builder, FeatureConfig featureConfig, string baseDirectory)
    {
        // featureConfig.Id is guaranteed non-null/whitespace here: the only caller (the
        // Features loop in BuildPackageModel) already returns JSN009 before invoking this
        // method when Id is null or whitespace.
        builder.Feature(featureConfig.Id!, fb =>
        {
            if (!string.IsNullOrWhiteSpace(featureConfig.Title))
                fb.Title = featureConfig.Title;

            if (!string.IsNullOrWhiteSpace(featureConfig.Description))
                fb.Description = featureConfig.Description;

            fb.IsDefault = featureConfig.Default;
            fb.IsRequired = featureConfig.Required;

            // Files
            if (featureConfig.Files is not null && featureConfig.Files.Count > 0)
            {
                fb.Files(fs =>
                {
                    fs.To(builder.DefaultInstallDirectory ?? KnownFolder.ProgramFiles / builder.Name);
                    foreach (var file in featureConfig.Files)
                    {
                        if (!string.IsNullOrWhiteSpace(file.Source))
                        {
                            var resolvedPath = ResolvePath(file.Source, baseDirectory);
                            fs.Add(resolvedPath);
                        }
                    }
                });
            }

            // Nested features
            if (featureConfig.Features is not null)
            {
                foreach (var childFeature in featureConfig.Features)
                {
                    if (!string.IsNullOrWhiteSpace(childFeature.Id))
                    {
                        fb.Feature(childFeature.Id, cfb =>
                        {
                            ConfigureNestedFeature(cfb, childFeature, baseDirectory, builder.DefaultInstallDirectory ?? KnownFolder.ProgramFiles / builder.Name);
                        });
                    }
                }
            }
        });

        // Shortcuts (must be configured after feature, at PackageBuilder level)
        if (featureConfig.Files is not null)
        {
            foreach (var file in featureConfig.Files)
            {
                if (file.Shortcut is not null && !string.IsNullOrWhiteSpace(file.Shortcut.Name) && !string.IsNullOrWhiteSpace(file.Source))
                {
                    var shortcutBuilder = builder.Shortcut(file.Shortcut.Name, Path.GetFileName(file.Source));

                    if (!string.IsNullOrWhiteSpace(file.Shortcut.Description))
                        shortcutBuilder.WithDescription(file.Shortcut.Description);

                    if (!string.IsNullOrWhiteSpace(file.Shortcut.Icon))
                        shortcutBuilder.WithIcon(ResolvePath(file.Shortcut.Icon, baseDirectory));

                    var location = file.Shortcut.Location?.ToLowerInvariant() ?? "desktop";
                    switch (location)
                    {
                        case "desktop":
                            shortcutBuilder.OnDesktop();
                            break;
                        case "startmenu":
                            shortcutBuilder.OnStartMenu();
                            break;
                        case "startup":
                            shortcutBuilder.OnStartup();
                            break;
                        default:
                            shortcutBuilder.OnDesktop();
                            break;
                    }
                }
            }
        }

        // Registry
        if (featureConfig.Registry is not null)
        {
            foreach (var reg in featureConfig.Registry)
            {
                if (!string.IsNullOrWhiteSpace(reg.Key) && !string.IsNullOrWhiteSpace(reg.Name))
                {
                    var root = ParseRegistryRoot(reg.Root);
                    builder.Registry(rb => rb.Key(root, reg.Key, kb => kb.Value(reg.Name, reg.Value ?? "")));
                }
            }
        }

        // Services
        if (featureConfig.Services is not null)
        {
            foreach (var svc in featureConfig.Services)
            {
                if (!string.IsNullOrWhiteSpace(svc.Name) && !string.IsNullOrWhiteSpace(svc.Executable))
                {
                    builder.Service(svc.Name, sb =>
                    {
                        sb.Executable = svc.Executable;
                        if (!string.IsNullOrWhiteSpace(svc.DisplayName))
                            sb.DisplayName = svc.DisplayName;
                        if (!string.IsNullOrWhiteSpace(svc.Description))
                            sb.Description = svc.Description;
                        if (!string.IsNullOrWhiteSpace(svc.StartType) &&
                            Enum.TryParse<ServiceStartMode>(svc.StartType, true, out var startMode))
                            sb.StartMode = startMode;
                        if (!string.IsNullOrWhiteSpace(svc.Account) &&
                            Enum.TryParse<ServiceAccount>(svc.Account, true, out var account))
                            sb.Account = account;
                    });
                }
            }
        }

        // Environment Variables
        if (featureConfig.EnvironmentVariables is not null)
        {
            foreach (var env in featureConfig.EnvironmentVariables)
            {
                if (!string.IsNullOrWhiteSpace(env.Name) && !string.IsNullOrWhiteSpace(env.Value))
                {
                    builder.EnvironmentVariable(env.Name, env.Value, evb =>
                    {
                        evb.IsSystem = env.System;
                        if (!string.IsNullOrWhiteSpace(env.Action) &&
                            Enum.TryParse<EnvironmentVariableAction>(env.Action, true, out var action))
                            evb.Action = action;
                    });
                }
            }
        }

        return Result<Unit>.Success(Unit.Value);
    }

    private static void ConfigureNestedFeature(FeatureBuilder fb, FeatureConfig config, string baseDirectory, InstallPath installDirectory)
    {
        if (!string.IsNullOrWhiteSpace(config.Title))
            fb.Title = config.Title;

        if (!string.IsNullOrWhiteSpace(config.Description))
            fb.Description = config.Description;

        fb.IsDefault = config.Default;
        fb.IsRequired = config.Required;

        if (config.Files is not null && config.Files.Count > 0)
        {
            fb.Files(fs =>
            {
                fs.To(installDirectory);
                foreach (var file in config.Files)
                {
                    if (!string.IsNullOrWhiteSpace(file.Source))
                    {
                        var resolvedPath = ResolvePath(file.Source, baseDirectory);
                        fs.Add(resolvedPath);
                    }
                }
            });
        }

        if (config.Features is not null)
        {
            foreach (var childFeature in config.Features)
            {
                if (!string.IsNullOrWhiteSpace(childFeature.Id))
                {
                    fb.Feature(childFeature.Id, cfb =>
                    {
                        ConfigureNestedFeature(cfb, childFeature, baseDirectory, installDirectory);
                    });
                }
            }
        }
    }

    private static RegistryRoot ParseRegistryRoot(string? root)
    {
        return root?.ToUpperInvariant() switch
        {
            "HKLM" or "LOCALMACHINE" => RegistryRoot.LocalMachine,
            "HKCU" or "CURRENTUSER" => RegistryRoot.CurrentUser,
            "HKCR" or "CLASSESROOT" => RegistryRoot.ClassesRoot,
            "HKU" or "USERS" => RegistryRoot.Users,
            _ => RegistryRoot.LocalMachine,
        };
    }

    /// <summary>
    /// True when the extension block declares any actual firewall/IIS/SQL/.NET content (as opposed
    /// to an empty or all-null <c>extensions</c> object). Used to decide whether the not-yet-applied
    /// authoring must fail loud (JSN019); an empty block is a no-op and must not trip the guard.
    /// </summary>
    private static bool HasAnyExtensionContent(ExtensionsConfig extensions)
        => extensions.Firewall is { Count: > 0 }
        || (extensions.Iis is not null
            && ((extensions.Iis.AppPools is { Count: > 0 }) || (extensions.Iis.WebSites is { Count: > 0 })))
        || extensions.Sql is { Count: > 0 }
        || extensions.DotNet is { Count: > 0 };

    private static Result<Unit> ValidateExtensions(ExtensionsConfig extensions)
    {
        // Firewall rules
        if (extensions.Firewall is not null)
        {
            for (var i = 0; i < extensions.Firewall.Count; i++)
            {
                var rule = extensions.Firewall[i];

                if (string.IsNullOrWhiteSpace(rule.Id))
                    return Result<Unit>.Failure(new Error(ErrorKind.Validation, $"JSN011: Firewall rule at index {i} is missing required field 'id'"));

                if (string.IsNullOrWhiteSpace(rule.Name))
                    return Result<Unit>.Failure(new Error(ErrorKind.Validation, $"JSN011: Firewall rule '{rule.Id}' is missing required field 'name'"));

                if (string.IsNullOrWhiteSpace(rule.Port) && string.IsNullOrWhiteSpace(rule.Program))
                    return Result<Unit>.Failure(new Error(ErrorKind.Validation, $"JSN011: Firewall rule '{rule.Id}' must specify either 'port' or 'program'"));
            }
        }

        // IIS
        if (extensions.Iis is not null)
        {
            if (extensions.Iis.AppPools is not null)
            {
                for (var i = 0; i < extensions.Iis.AppPools.Count; i++)
                {
                    var pool = extensions.Iis.AppPools[i];

                    if (string.IsNullOrWhiteSpace(pool.Name))
                        return Result<Unit>.Failure(new Error(ErrorKind.Validation, $"JSN012: IIS app pool at index {i} is missing required field 'name'"));
                }
            }

            if (extensions.Iis.WebSites is not null)
            {
                for (var i = 0; i < extensions.Iis.WebSites.Count; i++)
                {
                    var site = extensions.Iis.WebSites[i];

                    if (string.IsNullOrWhiteSpace(site.Description))
                        return Result<Unit>.Failure(new Error(ErrorKind.Validation, $"JSN012: IIS web site at index {i} is missing required field 'description'"));

                    if (site.Bindings is null || site.Bindings.Count == 0)
                        return Result<Unit>.Failure(new Error(ErrorKind.Validation, $"JSN012: IIS web site '{site.Description}' must have at least one binding"));
                }
            }
        }

        // SQL
        if (extensions.Sql is not null)
        {
            for (var i = 0; i < extensions.Sql.Count; i++)
            {
                var sql = extensions.Sql[i];

                if (string.IsNullOrWhiteSpace(sql.Server))
                    return Result<Unit>.Failure(new Error(ErrorKind.Validation, $"JSN013: SQL configuration at index {i} is missing required field 'server'"));

                if (string.IsNullOrWhiteSpace(sql.Database))
                    return Result<Unit>.Failure(new Error(ErrorKind.Validation, $"JSN013: SQL configuration at index {i} is missing required field 'database'"));
            }
        }

        // .NET detection
        if (extensions.DotNet is not null)
        {
            for (var i = 0; i < extensions.DotNet.Count; i++)
            {
                var dotnet = extensions.DotNet[i];

                if (string.IsNullOrWhiteSpace(dotnet.RuntimeType))
                    return Result<Unit>.Failure(new Error(ErrorKind.Validation, $"JSN014: .NET detection at index {i} is missing required field 'runtimeType'"));

                if (string.IsNullOrWhiteSpace(dotnet.Platform))
                    return Result<Unit>.Failure(new Error(ErrorKind.Validation, $"JSN014: .NET detection at index {i} is missing required field 'platform'"));

                if (string.IsNullOrWhiteSpace(dotnet.MinimumVersion))
                    return Result<Unit>.Failure(new Error(ErrorKind.Validation, $"JSN014: .NET detection at index {i} is missing required field 'minimumVersion'"));

                if (string.IsNullOrWhiteSpace(dotnet.VariableName))
                    return Result<Unit>.Failure(new Error(ErrorKind.Validation, $"JSN014: .NET detection at index {i} is missing required field 'variableName'"));
            }
        }

        return Result<Unit>.Success(Unit.Value);
    }

    private static string ResolvePath(string path, string baseDirectory)
    {
        if (Path.IsPathRooted(path))
            return path;
        return Path.GetFullPath(Path.Combine(baseDirectory, path));
    }
}
