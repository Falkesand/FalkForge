namespace FalkForge.Extensibility;

using System.Reflection;

/// <summary>
/// Provides the current FalkForge host version used for extension compatibility checks
/// (<see cref="IFalkForgeExtension.MinHostVersion"/>). The version is read from the
/// host assembly's <see cref="AssemblyInformationalVersionAttribute"/> the first time
/// it is requested, with a stable fallback so unit tests and unsigned builds still
/// produce a parseable <see cref="System.Version"/>.
/// </summary>
public static class FalkForgeVersion
{
    /// <summary>
    /// Stable fallback returned when the host assembly does not carry an
    /// <see cref="AssemblyInformationalVersionAttribute"/> or when its value cannot be
    /// parsed as a <see cref="System.Version"/>. Matches the long-standing
    /// <c>ExtensionRegistration.CurrentHostVersion</c> constant.
    /// </summary>
    public const string Fallback = "1.0.0";

    private static readonly Lazy<string> CurrentLazy = new(ResolveCurrent);

    /// <summary>
    /// Current host version as a dotted numeric string (e.g. <c>"1.0.0"</c>). Always
    /// parseable by <see cref="System.Version.TryParse(string?, out System.Version?)"/>.
    /// </summary>
    public static string Current => CurrentLazy.Value;

    private static string ResolveCurrent()
    {
        var attr = typeof(FalkForgeVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (attr is null)
        {
            return Fallback;
        }

        // Strip Source Link metadata suffix ("1.2.3+abcdef") so System.Version can parse it.
        var raw = attr.InformationalVersion;
        var plusIndex = raw.IndexOf('+', StringComparison.Ordinal);
        var trimmed = plusIndex >= 0 ? raw[..plusIndex] : raw;

        // Strip pre-release tag ("1.2.3-rc1") for the same reason.
        var dashIndex = trimmed.IndexOf('-', StringComparison.Ordinal);
        if (dashIndex >= 0)
        {
            trimmed = trimmed[..dashIndex];
        }

        return Version.TryParse(trimmed, out _) ? trimmed : Fallback;
    }
}
