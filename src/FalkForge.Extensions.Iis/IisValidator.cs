using FalkForge.Extensions.Iis.Models;

namespace FalkForge.Extensions.Iis;

public static class IisValidator
{
    public static Result<Unit> ValidateWebSites(IReadOnlyList<WebSiteModel> webSites)
    {
        foreach (var site in webSites)
        {
            var result = ValidateWebSite(site);
            if (result.IsFailure)
                return result;
        }

        return Unit.Value;
    }

    public static Result<Unit> ValidateWebSite(WebSiteModel site)
    {
        if (string.IsNullOrWhiteSpace(site.Description))
            return Result<Unit>.Failure(ErrorKind.Validation, "IIS001: WebSite must have a Description.");

        if (site.Bindings.Count == 0)
            return Result<Unit>.Failure(ErrorKind.Validation, $"IIS002: WebSite '{site.Description}' must have at least one binding.");

        foreach (var binding in site.Bindings)
        {
            var bindingResult = ValidateBinding(binding, site.Description);
            if (bindingResult.IsFailure)
                return bindingResult;
        }

        return Unit.Value;
    }

    public static Result<Unit> ValidateBinding(WebBindingModel binding, string siteDescription)
    {
        if (binding.Port <= 0 || binding.Port > 65535)
            return Result<Unit>.Failure(ErrorKind.Validation, $"IIS003: Binding on site '{siteDescription}' must have a valid Port (1-65535).");

        if (string.Equals(binding.Protocol, "https", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(binding.CertificateRef))
            return Result<Unit>.Failure(ErrorKind.Validation, $"IIS004: HTTPS binding on site '{siteDescription}' must have a CertificateRef.");

        return Unit.Value;
    }

    public static Result<Unit> ValidateAppPools(IReadOnlyList<AppPoolModel> appPools)
    {
        foreach (var pool in appPools)
        {
            var result = ValidateAppPool(pool);
            if (result.IsFailure)
                return result;
        }

        return Unit.Value;
    }

    public static Result<Unit> ValidateAppPool(AppPoolModel pool)
    {
        if (string.IsNullOrWhiteSpace(pool.Name))
            return Result<Unit>.Failure(ErrorKind.Validation, "IIS005: AppPool must have a Name.");

        if (pool.IdentityType == AppPoolIdentityType.SpecificUser && string.IsNullOrWhiteSpace(pool.UserName))
            return Result<Unit>.Failure(ErrorKind.Validation, $"IIS006: AppPool '{pool.Name}' uses SpecificUser identity but no UserName specified.");

        if (pool.IdentityType == AppPoolIdentityType.SpecificUser && string.IsNullOrWhiteSpace(pool.Password))
            return Result<Unit>.Failure(ErrorKind.Validation, $"IIS009: AppPool '{pool.Name}' uses SpecificUser identity but no Password specified.");

        return Unit.Value;
    }

    public static Result<Unit> ValidateCertificates(IReadOnlyList<CertificateModel> certificates)
    {
        foreach (var cert in certificates)
        {
            var result = ValidateCertificate(cert);
            if (result.IsFailure)
                return result;
        }

        return Unit.Value;
    }

    public static Result<Unit> ValidateCertificate(CertificateModel certificate)
    {
        if (string.IsNullOrWhiteSpace(certificate.Id))
            return Result<Unit>.Failure(ErrorKind.Validation, "IIS007: Certificate must have an Id.");

        if (string.IsNullOrWhiteSpace(certificate.FindValue))
            return Result<Unit>.Failure(ErrorKind.Validation, $"IIS008: Certificate '{certificate.Id}' must have a FindValue.");

        return Unit.Value;
    }

    public static Result<Unit> ValidateAll(
        IReadOnlyList<WebSiteModel> webSites,
        IReadOnlyList<AppPoolModel> appPools,
        IReadOnlyList<CertificateModel> certificates)
    {
        var poolResult = ValidateAppPools(appPools);
        if (poolResult.IsFailure)
            return poolResult;

        var certResult = ValidateCertificates(certificates);
        if (certResult.IsFailure)
            return certResult;

        var siteResult = ValidateWebSites(webSites);
        if (siteResult.IsFailure)
            return siteResult;

        var refResult = ValidateRefs(webSites, appPools, certificates);
        if (refResult.IsFailure)
            return refResult;

        return Unit.Value;
    }

    public static Result<Unit> ValidateRefs(
        IReadOnlyList<WebSiteModel> webSites,
        IReadOnlyList<AppPoolModel> appPools,
        IReadOnlyList<CertificateModel> certificates)
    {
        var poolIds = new HashSet<string>(appPools.Select(p => p.Id));
        var certIds = new HashSet<string>(certificates.Select(c => c.Id));

        foreach (var site in webSites)
        {
            if (site.AppPool is not null && !poolIds.Contains(site.AppPool))
                return Result<Unit>.Failure(ErrorKind.Validation,
                    $"IIS010: WebSite '{site.Description}' references undefined app pool '{site.AppPool}'.");

            foreach (var app in site.WebApplications)
            {
                if (app.AppPool is not null && !poolIds.Contains(app.AppPool))
                    return Result<Unit>.Failure(ErrorKind.Validation,
                        $"IIS010: WebApplication '{app.Alias}' on site '{site.Description}' references undefined app pool '{app.AppPool}'.");
            }

            foreach (var binding in site.Bindings)
            {
                if (binding.CertificateRef is not null && !certIds.Contains(binding.CertificateRef))
                    return Result<Unit>.Failure(ErrorKind.Validation,
                        $"IIS011: Binding on site '{site.Description}' references undefined certificate '{binding.CertificateRef}'.");
            }
        }

        return Unit.Value;
    }
}
