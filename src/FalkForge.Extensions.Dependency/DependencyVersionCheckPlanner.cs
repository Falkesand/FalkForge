using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FalkForge.Extensions.Dependency;

/// <summary>
///     Plans the MSI-time enforcement of dependency consumer version ranges. A consumer that
///     declares only presence (no <see cref="DependencyConsumerModel.MinVersion"/> or
///     <see cref="DependencyConsumerModel.MaxVersion"/>) is out of scope here — that case has
///     no version comparison to perform at install time and is left to author-time validation
///     (DEP-series rules) and the design-time <see cref="DependencyChecker"/>.
///     <para>
///     For every consumer with a version-range constraint, the planner reuses
///     <see cref="DependencyChecker.BuildRange"/> to derive the exact same effective range the
///     design-time checker would compute, then renders it into: a synthetic AppSearch property
///     name, the provider's registry key path (matching <see cref="DependencyTableContributor"/>'s
///     layout exactly), and an immediate JScript custom action that reads the property and does a
///     REAL component-wise numeric comparison. MSI condition-expression operators compare
///     lexicographically (so <c>"10.0.0.0" &gt;= "9.0.0.0"</c> is false), which is why the check
///     cannot be a static LaunchCondition and must run code — the JScript mirrors
///     <see cref="VersionRange.IsSatisfiedBy"/>.
///     </para>
///     <para>
///     Identifiers are salted with a content hash of <c>ProviderKey|ConsumerKey</c> rather than a
///     positional index so two <see cref="DependencyExtension"/> instances in one package cannot
///     collide on <c>FALKDEP0</c> (only a genuinely duplicate requirement collides, which is a
///     real authoring error worth surfacing).
///     </para>
///     <para>
///     Version-comparison fidelity note: <see cref="VersionRange"/> uses
///     <see cref="Version.CompareTo(Version)"/>, where an unspecified Build/Revision sorts as -1
///     (below 0). The emitted JScript treats a missing component of the runtime-read value as 0.
///     The compile-time bounds are normalized to four zero-filled components, so the only
///     divergence is a provider that publishes a 2- or 3-part version string sitting exactly on an
///     exclusive boundary — an edge case where treating missing components as 0 is the more
///     intuitive behavior.
///     </para>
/// </summary>
internal static class DependencyVersionCheckPlanner
{
    // Sequence numbers: after AppSearch (50) / LaunchConditions (100) which populate the property,
    // and well before InstallInitialize (1500) which is where the install begins committing.
    private const int FirstEvalSequence = 101;

    // CustomAction.Target is a CHAR(255) column; a longer message would fail msi.dll insertion.
    // The abort message is display-only, so a defensive cap (cosmetic truncation) is preferable to
    // a build failure when provider/consumer keys are very long.
    private const int MaxMessageLength = 255;

    internal static IReadOnlyList<DependencyVersionCheck> Plan(IReadOnlyList<DependencyConsumerModel> consumers)
    {
        if (consumers.Count == 0)
            return [];

        var plan = new List<DependencyVersionCheck>();
        var index = 0;

        foreach (var consumer in consumers)
        {
            if (consumer.MinVersion is null && consumer.MaxVersion is null)
                continue; // Presence-only requirement: no version comparison to enforce here.

            var range = DependencyChecker.BuildRange(consumer);
            if (range.MinVersion is null && range.MaxVersion is null)
                continue; // Both bounds were unparseable; DEP008 flags this at author time.

            var suffix = Suffix(consumer);
            var property = "FALKDEP" + suffix;
            var failProperty = "FALKDEPF" + suffix;
            var signature = "FalkDepSig" + suffix;
            var binaryName = "FalkDepBin" + suffix;
            var evalAction = "FalkDepChk" + suffix;
            var abortAction = "FalkDepErr" + suffix;
            var keyPath = @$"SOFTWARE\Classes\Installer\Dependencies\{consumer.ProviderKey}";

            var script = BuildScript(property, failProperty, range);
            var message = BuildMessage(consumer, range, property);
            var evalSeq = FirstEvalSequence + (index * 2);
            var abortSeq = evalSeq + 1;

            plan.Add(new DependencyVersionCheck(
                property,
                failProperty,
                signature,
                keyPath,
                binaryName,
                Encoding.UTF8.GetBytes(script),
                evalAction,
                evalSeq,
                abortAction,
                abortSeq,
                message));
            index++;
        }

        return plan;
    }

    /// <summary>
    ///     Stable 8-hex-char content hash of the provider+consumer keys, used to salt the synthetic
    ///     MSI identifiers so they are unique per requirement and collision-free across multiple
    ///     extension instances.
    /// </summary>
    private static string Suffix(DependencyConsumerModel consumer)
    {
        var material = $"{consumer.ProviderKey} {consumer.ConsumerKey}";
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(material), hash);
        return Convert.ToHexStringLower(hash[..4]);
    }

    /// <summary>
    ///     Builds the immediate-JScript body: reads the property, performs a component-wise numeric
    ///     comparison identical to <see cref="VersionRange.IsSatisfiedBy"/>, and sets the fail
    ///     property when unsatisfied (including the missing-provider case where the property reads
    ///     as empty). Only numeric bounds and controlled identifiers are interpolated — no
    ///     user-authored free text reaches the script body, so there is no code-injection surface.
    /// </summary>
    private static string BuildScript(string property, string failProperty, VersionRange range)
    {
        var sb = new StringBuilder(512);
        sb.Append("var v=Session.Property(\"").Append(property).Append("\");var f=0;");
        sb.Append("function A(s){var p=(\"\"+s).split(\".\");var r=[0,0,0,0];")
          .Append("for(var i=0;i<4;i++){var n=parseInt(p[i],10);r[i]=isNaN(n)?0:n;}return r;}");
        sb.Append("function C(a,b){for(var i=0;i<4;i++){if(a[i]<b[i])return -1;if(a[i]>b[i])return 1;}return 0;}");
        sb.Append("if(v==\"\"){f=1;}else{var c=A(v);");

        if (range.MinVersion is not null)
        {
            // Inclusive min: fail if current < min (C < 0). Exclusive min: fail if current <= min (C <= 0).
            var op = range.MinInclusive ? "<0" : "<=0";
            sb.Append("if(C(c,A(\"").Append(FormatVersion(range.MinVersion)).Append("\"))").Append(op).Append(")f=1;");
        }

        if (range.MaxVersion is not null)
        {
            // Inclusive max: fail if current > max (C > 0). Exclusive max: fail if current >= max (C >= 0).
            var op = range.MaxInclusive ? ">0" : ">=0";
            sb.Append("if(C(c,A(\"").Append(FormatVersion(range.MaxVersion)).Append("\"))").Append(op).Append(")f=1;");
        }

        sb.Append("}if(f){Session.Property(\"").Append(failProperty).Append("\")=\"1\";}");
        return sb.ToString();
    }

    private static string BuildMessage(DependencyConsumerModel consumer, VersionRange range, string property)
    {
        var providerKey = EscapeFormattedText(consumer.ProviderKey);
        var consumerKey = EscapeFormattedText(consumer.ConsumerKey);
        var rangeText = FormatRangeText(range);

        var message =
            $"Dependency '{providerKey}' (required by '{consumerKey}') must be installed in version " +
            $"range {rangeText}; the detected version was '[{property}]' (blank means not installed). " +
            "Install or upgrade the required provider, then run this installer again.";

        return message.Length <= MaxMessageLength
            ? message
            : message[..(MaxMessageLength - 1)] + "…";
    }

    /// <summary>
    ///     Normalizes a <see cref="Version"/> to a full four-part string (missing Build/Revision
    ///     become 0) so the JScript comparison operands are unambiguous.
    /// </summary>
    private static string FormatVersion(Version version)
        => new Version(
            version.Major,
            version.Minor,
            Math.Max(version.Build, 0),
            Math.Max(version.Revision, 0)).ToString(4);

    private static string FormatRangeText(VersionRange range)
    {
        if (range.MinVersion is not null && range.MaxVersion is not null)
        {
            var open = range.MinInclusive ? '[' : '(';
            var close = range.MaxInclusive ? ']' : ')';
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}{1}, {2}{3}",
                open, FormatVersion(range.MinVersion), FormatVersion(range.MaxVersion), close);
        }

        if (range.MinVersion is not null)
            return (range.MinInclusive ? "at least " : "greater than ") + FormatVersion(range.MinVersion);

        // range.MaxVersion is guaranteed non-null here — Plan() skips consumers with both bounds null.
        return (range.MaxInclusive ? "at most " : "less than ") + FormatVersion(range.MaxVersion!);
    }

    /// <summary>
    ///     Escapes MSI formatted-text bracket metacharacters so a provider/consumer key cannot be
    ///     crafted to inject a spurious <c>[Property]</c> reference into the abort message shown to
    ///     the user (OWASP: no injection via authored identifiers).
    /// </summary>
    private static string EscapeFormattedText(string value)
        => value.Replace("[", "[\\[]", StringComparison.Ordinal).Replace("]", "[\\]]", StringComparison.Ordinal);
}
