using System.Text.Json;
using System.Text.Json.Serialization;
using FalkForge.Cli.Models;

namespace FalkForge.Cli;

/// <summary>
/// AOT-safe, allocation-free source-generated JSON context for <c>forge build</c>'s JSON
/// installer config (<see cref="InstallerConfig"/> and its nested DTOs). Replaces the
/// reflection-based <see cref="JsonSerializer"/> path previously used by
/// <see cref="JsonConfigLoader"/>. The generation options below must stay in lockstep with
/// the runtime <c>JsonSerializerOptions</c> the loader used to construct manually
/// (case-insensitive property matching, comment skipping, trailing commas) so that every
/// JSON document parsed before continues to parse identically.
/// </summary>
[JsonSerializable(typeof(InstallerConfig))]
[JsonSerializable(typeof(ProductConfig))]
[JsonSerializable(typeof(MajorUpgradeConfig))]
[JsonSerializable(typeof(DowngradeConfig))]
[JsonSerializable(typeof(LaunchConditionConfig))]
[JsonSerializable(typeof(List<LaunchConditionConfig>))]
[JsonSerializable(typeof(FeatureConfig))]
[JsonSerializable(typeof(List<FeatureConfig>))]
[JsonSerializable(typeof(FileConfig))]
[JsonSerializable(typeof(List<FileConfig>))]
[JsonSerializable(typeof(ShortcutConfig))]
[JsonSerializable(typeof(RegistryConfig))]
[JsonSerializable(typeof(List<RegistryConfig>))]
[JsonSerializable(typeof(ServiceConfig))]
[JsonSerializable(typeof(List<ServiceConfig>))]
[JsonSerializable(typeof(EnvironmentVariableConfig))]
[JsonSerializable(typeof(List<EnvironmentVariableConfig>))]
[JsonSerializable(typeof(ExtensionsConfig))]
[JsonSerializable(typeof(FirewallRuleConfig))]
[JsonSerializable(typeof(List<FirewallRuleConfig>))]
[JsonSerializable(typeof(IisConfig))]
[JsonSerializable(typeof(IisAppPoolConfig))]
[JsonSerializable(typeof(List<IisAppPoolConfig>))]
[JsonSerializable(typeof(IisWebSiteConfig))]
[JsonSerializable(typeof(List<IisWebSiteConfig>))]
[JsonSerializable(typeof(IisBindingConfig))]
[JsonSerializable(typeof(List<IisBindingConfig>))]
[JsonSerializable(typeof(SqlConfig))]
[JsonSerializable(typeof(List<SqlConfig>))]
[JsonSerializable(typeof(SqlScriptConfig))]
[JsonSerializable(typeof(List<SqlScriptConfig>))]
[JsonSerializable(typeof(DotNetSearchConfig))]
[JsonSerializable(typeof(List<DotNetSearchConfig>))]
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
internal sealed partial class InstallerConfigJsonContext : JsonSerializerContext;
