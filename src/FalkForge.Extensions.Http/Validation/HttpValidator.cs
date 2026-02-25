using FalkForge.Extensions.Http.Models;

namespace FalkForge.Extensions.Http.Validation;

internal static class HttpValidator
{
    internal static Result<Unit> ValidateReservations(IReadOnlyList<UrlReservationModel> reservations)
    {
        foreach (var r in reservations)
        {
            var result = ValidateReservation(r);
            if (result.IsFailure)
                return result;
        }

        return Unit.Value;
    }

    internal static Result<Unit> ValidateReservation(UrlReservationModel r)
    {
        if (string.IsNullOrWhiteSpace(r.Url))
            return Result<Unit>.Failure(ErrorKind.Validation, "HTTP001: URL reservation URL must not be empty.");

        if (!r.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !r.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return Result<Unit>.Failure(ErrorKind.Validation, $"HTTP002: URL reservation URL '{r.Url}' must start with http:// or https://.");

        if (!r.Url.EndsWith('/'))
            return Result<Unit>.Failure(ErrorKind.Validation, $"HTTP003: URL reservation URL '{r.Url}' must end with '/'.");

        if (string.IsNullOrWhiteSpace(r.User))
            return Result<Unit>.Failure(ErrorKind.Validation, "HTTP004: URL reservation User/SDDL string must not be empty.");

        return Unit.Value;
    }

    internal static Result<Unit> ValidateBindings(IReadOnlyList<SniSslBindingModel> bindings)
    {
        foreach (var b in bindings)
        {
            var result = ValidateBinding(b);
            if (result.IsFailure)
                return result;
        }

        return Unit.Value;
    }

    internal static Result<Unit> ValidateBinding(SniSslBindingModel b)
    {
        if (string.IsNullOrWhiteSpace(b.Hostname))
            return Result<Unit>.Failure(ErrorKind.Validation, "HTTP005: SNI SSL binding Hostname must not be empty.");

        if (b.Port is < 1 or > 65535)
            return Result<Unit>.Failure(ErrorKind.Validation, $"HTTP006: SNI SSL binding Port {b.Port} is outside valid range 1-65535.");

        if (string.IsNullOrWhiteSpace(b.CertificateThumbprint))
            return Result<Unit>.Failure(ErrorKind.Validation, "HTTP007: SNI SSL binding CertificateThumbprint must not be empty.");

        if (b.CertificateThumbprint.Length != 40 || !IsAllHex(b.CertificateThumbprint))
            return Result<Unit>.Failure(ErrorKind.Validation, $"HTTP008: SNI SSL binding CertificateThumbprint '{b.CertificateThumbprint}' must be exactly 40 hexadecimal characters.");

        if (b.AppId == Guid.Empty)
            return Result<Unit>.Failure(ErrorKind.Validation, "HTTP009: SNI SSL binding AppId must not be an empty GUID.");

        return Unit.Value;
    }

    private static bool IsAllHex(string s)
    {
        foreach (var c in s)
            if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F')))
                return false;
        return true;
    }
}
