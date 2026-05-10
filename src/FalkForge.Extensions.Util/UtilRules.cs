using System.Collections.Immutable;
using FalkForge.Extensions.Util.XmlConfig;
using FalkForge.Validation;

namespace FalkForge.Extensions.Util;

/// <summary>
/// Rules-as-data for the Util extension (XCF001–XCF009).
/// Rules close over the XmlConfig list owned by the extension instance.
/// USR001-USR002 are runtime-only (no stored model) and cannot be expressed as static ValidationRules.
/// </summary>
public static class UtilRules
{
    private const int MaxXPathLength = 4096;

    /// <summary>
    /// Builds the set of <see cref="ValidationRule"/> instances for the XmlConfig sub-system.
    /// </summary>
    public static ImmutableArray<ValidationRule> Build(Func<IReadOnlyList<XmlConfigModel>> getXmlConfigs)
    {
        return
        [
            new ValidationRule(
                new RuleId("XCF001"),
                Severity.Error,
                ModelSection.Extension_Util,
                "XPath expression must not be empty",
                "Each XmlConfig entry must have a non-empty XPath expression.",
                ctx => getXmlConfigs()
                    .Where(m => !string.IsNullOrWhiteSpace(m.Id) && string.IsNullOrWhiteSpace(m.XPath))
                    .Select(m => new Violation(
                        new RuleId("XCF001"), Severity.Error,
                        ModelPath.Root.Field("XmlConfig").Field(m.Id),
                        "XCF001: XPath expression must not be empty."))),

            new ValidationRule(
                new RuleId("XCF002"),
                Severity.Error,
                ModelSection.Extension_Util,
                "FilePath must not be empty",
                "Each XmlConfig entry must have a non-empty FilePath.",
                ctx => getXmlConfigs()
                    .Where(m => !string.IsNullOrWhiteSpace(m.Id) && string.IsNullOrWhiteSpace(m.FilePath))
                    .Select(m => new Violation(
                        new RuleId("XCF002"), Severity.Error,
                        ModelPath.Root.Field("XmlConfig").Field(m.Id),
                        "XCF002: FilePath must not be empty."))),

            new ValidationRule(
                new RuleId("XCF003"),
                Severity.Error,
                ModelSection.Extension_Util,
                "CreateElement action requires ElementName",
                "XmlConfig entries with CreateElement action must specify an ElementName.",
                ctx => getXmlConfigs()
                    .Where(m => !string.IsNullOrWhiteSpace(m.Id)
                                && m.Action == XmlConfigAction.CreateElement
                                && string.IsNullOrWhiteSpace(m.ElementName))
                    .Select(m => new Violation(
                        new RuleId("XCF003"), Severity.Error,
                        ModelPath.Root.Field("XmlConfig").Field(m.Id),
                        "XCF003: CreateElement action requires ElementName."))),

            new ValidationRule(
                new RuleId("XCF004"),
                Severity.Error,
                ModelSection.Extension_Util,
                "SetAttribute action requires AttributeName and Value",
                "XmlConfig entries with SetAttribute action must specify AttributeName and Value.",
                ctx => getXmlConfigs()
                    .Where(m => !string.IsNullOrWhiteSpace(m.Id)
                                && m.Action == XmlConfigAction.SetAttribute
                                && (string.IsNullOrWhiteSpace(m.AttributeName) || m.Value is null))
                    .Select(m => new Violation(
                        new RuleId("XCF004"), Severity.Error,
                        ModelPath.Root.Field("XmlConfig").Field(m.Id),
                        "XCF004: SetAttribute action requires AttributeName and Value."))),

            new ValidationRule(
                new RuleId("XCF005"),
                Severity.Error,
                ModelSection.Extension_Util,
                "XPath expression exceeds maximum length",
                $"XPath expressions must not exceed {MaxXPathLength} characters.",
                ctx => getXmlConfigs()
                    .Where(m => !string.IsNullOrWhiteSpace(m.Id)
                                && !string.IsNullOrWhiteSpace(m.XPath)
                                && m.XPath.Length > MaxXPathLength)
                    .Select(m => new Violation(
                        new RuleId("XCF005"), Severity.Error,
                        ModelPath.Root.Field("XmlConfig").Field(m.Id),
                        $"XCF005: XPath expression exceeds maximum length of {MaxXPathLength} characters."))),

            new ValidationRule(
                new RuleId("XCF006"),
                Severity.Error,
                ModelSection.Extension_Util,
                "DeleteAttribute action requires AttributeName",
                "XmlConfig entries with DeleteAttribute action must specify an AttributeName.",
                ctx => getXmlConfigs()
                    .Where(m => !string.IsNullOrWhiteSpace(m.Id)
                                && m.Action == XmlConfigAction.DeleteAttribute
                                && string.IsNullOrWhiteSpace(m.AttributeName))
                    .Select(m => new Violation(
                        new RuleId("XCF006"), Severity.Error,
                        ModelPath.Root.Field("XmlConfig").Field(m.Id),
                        "XCF006: DeleteAttribute action requires AttributeName."))),

            new ValidationRule(
                new RuleId("XCF007"),
                Severity.Error,
                ModelSection.Extension_Util,
                "SetValue action requires Value",
                "XmlConfig entries with SetValue action must specify a Value.",
                ctx => getXmlConfigs()
                    .Where(m => !string.IsNullOrWhiteSpace(m.Id)
                                && m.Action == XmlConfigAction.SetValue
                                && m.Value is null)
                    .Select(m => new Violation(
                        new RuleId("XCF007"), Severity.Error,
                        ModelPath.Root.Field("XmlConfig").Field(m.Id),
                        "XCF007: SetValue action requires Value."))),

            new ValidationRule(
                new RuleId("XCF008"),
                Severity.Error,
                ModelSection.Extension_Util,
                "BulkSetValue action requires Value",
                "XmlConfig entries with BulkSetValue action must specify a Value.",
                ctx => getXmlConfigs()
                    .Where(m => !string.IsNullOrWhiteSpace(m.Id)
                                && m.Action == XmlConfigAction.BulkSetValue
                                && m.Value is null)
                    .Select(m => new Violation(
                        new RuleId("XCF008"), Severity.Error,
                        ModelPath.Root.Field("XmlConfig").Field(m.Id),
                        "XCF008: BulkSetValue action requires Value."))),

            new ValidationRule(
                new RuleId("XCF009"),
                Severity.Error,
                ModelSection.Extension_Util,
                "Duplicate XmlConfig Id",
                "Each XmlConfig entry must have a unique Id.",
                ctx =>
                {
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    return getXmlConfigs()
                        .Where(m => !string.IsNullOrWhiteSpace(m.Id) && !seen.Add(m.Id))
                        .Select(m => new Violation(
                            new RuleId("XCF009"), Severity.Error,
                            ModelPath.Root.Field("XmlConfig").Field(m.Id),
                            $"XCF009: Duplicate XmlConfig Id '{m.Id}'."));
                }),
        ];
    }
}
