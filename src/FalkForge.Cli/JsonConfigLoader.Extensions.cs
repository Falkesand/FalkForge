using System.Runtime.Versioning;
using FalkForge;
using FalkForge.Cli.Models;
using FalkForge.Extensibility;
using FalkForge.Extensions.DotNet;
using FalkForge.Extensions.Firewall;
using FalkForge.Extensions.Iis;
using FalkForge.Extensions.Iis.Models;
using FalkForge.Extensions.Sql.Builders;

namespace FalkForge.Cli;

public static partial class JsonConfigLoader
{
    /// <summary>
    /// Translates a validated <see cref="ExtensionsConfig"/> into real extension instances. Reuses the
    /// extensions' own fluent builders (never re-implements their MSI emission), so the JSON path and the
    /// C# path produce identical tables. A <c>dotnet</c> block builds a real <see cref="DotNetExtension"/>
    /// with MSI-native detection (<c>Signature</c>/<c>DrLocator</c>/<c>AppSearch</c>) and its own
    /// <c>LaunchCondition</c> — the JSON path has no separate call to gate on the detected property the
    /// way <c>package.Require(...)</c> does for the C# fluent path, so <c>BuildDotNet</c> always supplies
    /// a message (author-provided or a sensible default).
    /// </summary>
    private static Result<IReadOnlyList<IFalkForgeExtension>> BuildExtensions(ExtensionsConfig extensions)
    {
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

        if (extensions.DotNet is { Count: > 0 })
        {
            var dotnet = BuildDotNet(extensions.DotNet);
            if (dotnet.IsFailure)
                return Result<IReadOnlyList<IFalkForgeExtension>>.Failure(dotnet.Error);
            result.Add(dotnet.Value);
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

    private static Result<DotNetExtension> BuildDotNet(List<DotNetSearchConfig> searches)
    {
        var extension = new DotNetExtension();

        foreach (var search in searches)
        {
            // runtimeType/platform/minimumVersion/variableName presence is guaranteed by
            // ValidateExtensions (JSN014); only their VALUES are checked here.
            if (!TryParseEnum(search.RuntimeType!, out DotNetRuntimeType runtimeType))
                return DotNetEnumFailure("runtimeType", search.RuntimeType!, Enum.GetNames<DotNetRuntimeType>());

            if (!TryParseEnum(search.Platform!, out DotNetPlatform platform))
                return DotNetEnumFailure("platform", search.Platform!, Enum.GetNames<DotNetPlatform>());

            if (!Version.TryParse(search.MinimumVersion, out var minimumVersion))
                return Result<DotNetExtension>.Failure(new Error(ErrorKind.Validation,
                    $"JSN014: .NET detection has invalid minimumVersion '{search.MinimumVersion}'. Expected a " +
                    "dotted version string (e.g. '8.0.0')."));

            // The JSON path has no separate call (like package.Require) to gate on the detected
            // property, so it always needs a LaunchCondition message — author-provided, or a
            // sensible default naming the exact requirement.
            var message = string.IsNullOrWhiteSpace(search.Message)
                ? $".NET {runtimeType} {minimumVersion}+ ({platform}) is required. Install it from " +
                  "https://dotnet.microsoft.com/download"
                : search.Message;

            var model = extension.SearchForRuntime()
                .RuntimeType(runtimeType)
                .Platform(platform)
                .MinVersion(minimumVersion)
                .Variable(search.VariableName!)
                .Message(message)
                .Build();
            if (model.IsFailure)
                return Result<DotNetExtension>.Failure(model.Error);

            var added = extension.AddSearch(model.Value);
            if (added.IsFailure)
                return Result<DotNetExtension>.Failure(added.Error);
        }

        return Result<DotNetExtension>.Success(extension);
    }

    private static Result<DotNetExtension> DotNetEnumFailure(string field, string value, string[] valid) =>
        Result<DotNetExtension>.Failure(new Error(ErrorKind.Validation,
            $"JSN014: .NET detection has invalid {field} '{value}'. Valid values: {string.Join(", ", valid)}"));

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
    /// True when the extension block declares any actual firewall/IIS/SQL/.NET content (as opposed
    /// to an empty or all-null <c>extensions</c> object). Decides whether <see cref="BuildExtensions"/>
    /// runs at all: an empty block short-circuits to an empty extension list rather than doing
    /// pointless work.
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
}
