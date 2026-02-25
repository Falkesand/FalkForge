using FalkForge;
using FalkForge.Extensions.Http.Models;

namespace FalkForge.Extensions.Http.Validation;

internal static class HttpValidator
{
    internal static IReadOnlyList<Error> ValidateReservations(IReadOnlyList<UrlReservationModel> reservations)
    {
        var errors = new List<Error>();
        foreach (var r in reservations)
        {
            if (string.IsNullOrWhiteSpace(r.Url))
            {
                errors.Add(new Error(ErrorKind.Validation, "HTTP001: URL reservation URL must not be empty."));
                continue;
            }

            if (!r.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !r.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                errors.Add(new Error(ErrorKind.Validation, $"HTTP002: URL reservation URL '{r.Url}' must start with http:// or https://."));

            if (!r.Url.EndsWith('/'))
                errors.Add(new Error(ErrorKind.Validation, $"HTTP003: URL reservation URL '{r.Url}' must end with '/'."));

            if (string.IsNullOrWhiteSpace(r.User))
                errors.Add(new Error(ErrorKind.Validation, "HTTP004: URL reservation User/SDDL string must not be empty."));
        }
        return errors;
    }

    internal static IReadOnlyList<Error> ValidateBindings(IReadOnlyList<SniSslBindingModel> bindings)
    {
        var errors = new List<Error>();
        foreach (var b in bindings)
        {
            if (string.IsNullOrWhiteSpace(b.Hostname))
                errors.Add(new Error(ErrorKind.Validation, "HTTP005: SNI SSL binding Hostname must not be empty."));

            if (b.Port is < 1 or > 65535)
                errors.Add(new Error(ErrorKind.Validation, $"HTTP006: SNI SSL binding Port {b.Port} is outside valid range 1-65535."));

            if (string.IsNullOrWhiteSpace(b.CertificateThumbprint))
                errors.Add(new Error(ErrorKind.Validation, "HTTP007: SNI SSL binding CertificateThumbprint must not be empty."));
            else if (b.CertificateThumbprint.Length != 40 || !IsAllHex(b.CertificateThumbprint))
                errors.Add(new Error(ErrorKind.Validation, $"HTTP008: SNI SSL binding CertificateThumbprint '{b.CertificateThumbprint}' must be exactly 40 hexadecimal characters."));

            if (b.AppId == Guid.Empty)
                errors.Add(new Error(ErrorKind.Validation, "HTTP009: SNI SSL binding AppId must not be an empty GUID."));
        }
        return errors;
    }

    private static bool IsAllHex(string s)
    {
        foreach (var c in s)
            if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F')))
                return false;
        return true;
    }
}
