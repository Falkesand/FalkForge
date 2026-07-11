using System.Collections.Immutable;
using FalkForge.Extensions.Iis.Models;
using FalkForge.Validation;

namespace FalkForge.Extensions.Iis;

/// <summary>
/// Rules-as-data for the IIS extension (IIS001–IIS011).
/// Rules are built per-extension-instance so they close over the site/pool/certificate lists.
/// </summary>
public static class IisRules
{
    /// <summary>
    /// Builds the full set of <see cref="ValidationRule"/> instances for one <see cref="IisExtension"/>.
    /// </summary>
    public static ImmutableArray<ValidationRule> Build(
        Func<IReadOnlyList<WebSiteModel>> getWebSites,
        Func<IReadOnlyList<AppPoolModel>> getAppPools,
        Func<IReadOnlyList<CertificateModel>> getCertificates)
    {
        return
        [
            new ValidationRule(
                new RuleId("IIS001"),
                Severity.Error,
                ModelSection.Extension_Iis,
                "WebSite must have a Description",
                "Each IIS WebSite must have a non-empty Description.",
                ctx => getWebSites()
                    .Where(s => string.IsNullOrWhiteSpace(s.Description))
                    .Select(s => new Violation(
                        new RuleId("IIS001"), Severity.Error,
                        ModelPath.Root.Field("WebSite"),
                        "IIS001: WebSite must have a Description."))),

            new ValidationRule(
                new RuleId("IIS002"),
                Severity.Error,
                ModelSection.Extension_Iis,
                "WebSite must have at least one binding",
                "Each IIS WebSite must have at least one binding.",
                ctx => getWebSites()
                    .Where(s => !string.IsNullOrWhiteSpace(s.Description) && s.Bindings.Count == 0)
                    .Select(s => new Violation(
                        new RuleId("IIS002"), Severity.Error,
                        ModelPath.Root.Field("WebSite").Field(s.Description),
                        $"IIS002: WebSite '{s.Description}' must have at least one binding."))),

            new ValidationRule(
                new RuleId("IIS003"),
                Severity.Error,
                ModelSection.Extension_Iis,
                "WebSite binding must have a valid Port",
                "Each IIS binding must specify a Port between 1 and 65535.",
                ctx => getWebSites().SelectMany(s =>
                    s.Bindings
                        .Where(b => b.Port <= 0 || b.Port > 65535)
                        .Select(b => new Violation(
                            new RuleId("IIS003"), Severity.Error,
                            ModelPath.Root.Field("WebSite").Field(s.Description ?? string.Empty).Field("Binding"),
                            $"IIS003: Binding on site '{s.Description}' must have a valid Port (1-65535).")))),

            new ValidationRule(
                new RuleId("IIS004"),
                Severity.Error,
                ModelSection.Extension_Iis,
                "HTTPS binding must have a CertificateRef",
                "Each HTTPS binding must reference a certificate via CertificateRef.",
                ctx => getWebSites().SelectMany(s =>
                    s.Bindings
                        .Where(b => string.Equals(b.Protocol, "https", StringComparison.OrdinalIgnoreCase)
                                    && string.IsNullOrWhiteSpace(b.CertificateRef))
                        .Select(b => new Violation(
                            new RuleId("IIS004"), Severity.Error,
                            ModelPath.Root.Field("WebSite").Field(s.Description ?? string.Empty).Field("Binding"),
                            $"IIS004: HTTPS binding on site '{s.Description}' must have a CertificateRef.")))),

            new ValidationRule(
                new RuleId("IIS005"),
                Severity.Error,
                ModelSection.Extension_Iis,
                "AppPool must have a Name",
                "Each IIS AppPool must have a non-empty Name.",
                ctx => getAppPools()
                    .Where(p => string.IsNullOrWhiteSpace(p.Name))
                    .Select(p => new Violation(
                        new RuleId("IIS005"), Severity.Error,
                        ModelPath.Root.Field("AppPool"),
                        "IIS005: AppPool must have a Name."))),

            new ValidationRule(
                new RuleId("IIS006"),
                Severity.Error,
                ModelSection.Extension_Iis,
                "SpecificUser AppPool must have a UserName",
                "AppPools using SpecificUser identity must specify a UserName.",
                ctx => getAppPools()
                    .Where(p => p.IdentityType == AppPoolIdentityType.SpecificUser
                                && string.IsNullOrWhiteSpace(p.UserName))
                    .Select(p => new Violation(
                        new RuleId("IIS006"), Severity.Error,
                        ModelPath.Root.Field("AppPool").Field(p.Name ?? string.Empty),
                        $"IIS006: AppPool '{p.Name}' uses SpecificUser identity but no UserName specified."))),

            new ValidationRule(
                new RuleId("IIS007"),
                Severity.Error,
                ModelSection.Extension_Iis,
                "Certificate must have an Id",
                "Each IIS Certificate must have a non-empty Id.",
                ctx => getCertificates()
                    .Where(c => string.IsNullOrWhiteSpace(c.Id))
                    .Select(c => new Violation(
                        new RuleId("IIS007"), Severity.Error,
                        ModelPath.Root.Field("Certificate"),
                        "IIS007: Certificate must have an Id."))),

            new ValidationRule(
                new RuleId("IIS008"),
                Severity.Error,
                ModelSection.Extension_Iis,
                "Certificate must have a FindValue",
                "Each IIS Certificate must have a non-empty FindValue.",
                ctx => getCertificates()
                    .Where(c => !string.IsNullOrWhiteSpace(c.Id) && string.IsNullOrWhiteSpace(c.FindValue))
                    .Select(c => new Violation(
                        new RuleId("IIS008"), Severity.Error,
                        ModelPath.Root.Field("Certificate").Field(c.Id),
                        $"IIS008: Certificate '{c.Id}' must have a FindValue."))),

            new ValidationRule(
                new RuleId("IIS009"),
                Severity.Error,
                ModelSection.Extension_Iis,
                "SpecificUser AppPool must have a Password or PasswordProperty",
                "AppPools using SpecificUser identity must specify a Password or a PasswordProperty.",
                ctx => getAppPools()
                    .Where(p => p.IdentityType == AppPoolIdentityType.SpecificUser
                                && string.IsNullOrWhiteSpace(p.Password)
                                && string.IsNullOrWhiteSpace(p.PasswordProperty))
                    .Select(p => new Violation(
                        new RuleId("IIS009"), Severity.Error,
                        ModelPath.Root.Field("AppPool").Field(p.Name ?? string.Empty),
                        $"IIS009: AppPool '{p.Name}' uses SpecificUser identity but no Password or PasswordProperty specified."))),

            new ValidationRule(
                new RuleId("IIS010"),
                Severity.Error,
                ModelSection.Extension_Iis,
                "WebSite or WebApplication references an undefined AppPool",
                "AppPool references in WebSites and WebApplications must resolve to a defined AppPool.",
                ctx => ValidateAppPoolRefs(getWebSites(), getAppPools())),

            new ValidationRule(
                new RuleId("IIS011"),
                Severity.Error,
                ModelSection.Extension_Iis,
                "Binding references an undefined Certificate",
                "CertificateRef in bindings must resolve to a defined Certificate.",
                ctx => ValidateCertRefs(getWebSites(), getCertificates())),

            // Security warning: a literal SpecificUser password is embedded in plaintext in the MSI.
            new ValidationRule(
                new RuleId("IIS012"),
                Severity.Warning,
                ModelSection.Extension_Iis,
                "Literal IIS app-pool password embedded in the MSI",
                "A literal SpecificUser password is embedded in plaintext in the MSI. Use IdentitySecure(...) with a PasswordProperty populated by SetSecureProperty instead.",
                ctx => getAppPools()
                    .Where(p => !string.IsNullOrWhiteSpace(p.Name) && IisValidator.HasLiteralPassword(p))
                    .Select(p => new Violation(
                        new RuleId("IIS012"), Severity.Warning,
                        ModelPath.Root.Field("AppPool").Field(p.Name ?? string.Empty),
                        $"IIS012: AppPool '{p.Name}' embeds a literal password in plaintext in the MSI. " +
                        "Use IdentitySecure(...) with a PasswordProperty populated by SetSecureProperty instead."))),

            // Fail-loud deferrals: surface configuration that is authored but whose install-time execution is
            // NOT yet wired in this core branch, so an author is never silently misled into thinking it runs.

            new ValidationRule(
                new RuleId("IIS013"),
                Severity.Warning,
                ModelSection.Extension_Iis,
                "HTTPS/certificate binding runtime is not wired",
                "Certificate emission and SSL-certificate binding are deferred; an HTTPS/certificate binding is created without an SSL certificate at install.",
                ctx => getWebSites().SelectMany(s =>
                    s.Bindings
                        .Where(b => string.Equals(b.Protocol, "https", StringComparison.OrdinalIgnoreCase)
                                    || !string.IsNullOrWhiteSpace(b.CertificateRef))
                        .Select(b => new Violation(
                            new RuleId("IIS013"), Severity.Warning,
                            ModelPath.Root.Field("WebSite").Field(s.Description ?? string.Empty).Field("Binding"),
                            $"IIS013: HTTPS/certificate binding on site '{s.Description}' is authored, but certificate " +
                            "emission and SSL-certificate binding are NOT yet wired at install; the binding is skipped " +
                            "at runtime. Bind the certificate out-of-band, or apply it in a follow-up.")))),

            new ValidationRule(
                new RuleId("IIS014"),
                Severity.Warning,
                ModelSection.Extension_Iis,
                "WebApplication runtime is not wired",
                "Sub-application (WebApplication) creation is deferred; only the root site, its bindings, and its app pool are created at install.",
                ctx => getWebSites()
                    .Where(s => s.WebApplications.Count > 0)
                    .Select(s => new Violation(
                        new RuleId("IIS014"), Severity.Warning,
                        ModelPath.Root.Field("WebSite").Field(s.Description ?? string.Empty),
                        $"IIS014: WebSite '{s.Description}' authors sub-application(s), but WebApplication creation is " +
                        "NOT yet wired at install; only the root site, bindings, and app pool are created. " +
                        "Create sub-applications out-of-band, or apply them in a follow-up."))),
        ];
    }

    private static IEnumerable<Violation> ValidateAppPoolRefs(
        IReadOnlyList<WebSiteModel> webSites,
        IReadOnlyList<AppPoolModel> appPools)
    {
        var poolIds = new HashSet<string>(appPools.Select(p => p.Id), StringComparer.Ordinal);

        foreach (var site in webSites)
        {
            if (site.AppPool is not null && !poolIds.Contains(site.AppPool))
                yield return new Violation(
                    new RuleId("IIS010"), Severity.Error,
                    ModelPath.Root.Field("WebSite").Field(site.Description ?? string.Empty),
                    $"IIS010: WebSite '{site.Description}' references undefined app pool '{site.AppPool}'.");

            foreach (var app in site.WebApplications)
                if (app.AppPool is not null && !poolIds.Contains(app.AppPool))
                    yield return new Violation(
                        new RuleId("IIS010"), Severity.Error,
                        ModelPath.Root.Field("WebSite").Field(site.Description ?? string.Empty).Field("WebApplication").Field(app.Alias ?? string.Empty),
                        $"IIS010: WebApplication '{app.Alias}' on site '{site.Description}' references undefined app pool '{app.AppPool}'.");
        }
    }

    private static IEnumerable<Violation> ValidateCertRefs(
        IReadOnlyList<WebSiteModel> webSites,
        IReadOnlyList<CertificateModel> certificates)
    {
        var certIds = new HashSet<string>(certificates.Select(c => c.Id), StringComparer.Ordinal);

        foreach (var site in webSites)
        foreach (var binding in site.Bindings)
            if (binding.CertificateRef is not null && !certIds.Contains(binding.CertificateRef))
                yield return new Violation(
                    new RuleId("IIS011"), Severity.Error,
                    ModelPath.Root.Field("WebSite").Field(site.Description ?? string.Empty).Field("Binding"),
                    $"IIS011: Binding on site '{site.Description}' references undefined certificate '{binding.CertificateRef}'.");
    }
}
