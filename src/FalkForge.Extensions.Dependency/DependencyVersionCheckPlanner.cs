using System.Text;
using FalkForge.Extensibility;

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
///     <para>
///     Runtime prerequisites and fail-closed behavior: the evaluator is an immediate JScript
///     custom action, so it requires the Windows Script Host (present and enabled by default). On
///     a hardened image where script custom actions are disabled by policy, the action errors and
///     the install aborts — i.e. the gate fails <b>closed</b> (blocks) rather than silently
///     allowing an unverified dependency, which is the safe direction for a dependency gate. The
///     evaluator also fails closed when the provider's registered version is missing or
///     unparseable (treated as 0.0.0.0); this is deliberately stricter than the design-time
///     <see cref="DependencyChecker"/>, which is lenient toward an unparseable version because it
///     operates on the already-validated authored provider model rather than arbitrary machine
///     registry state.
///     </para>
/// </summary>
internal static class DependencyVersionCheckPlanner
{
    // Sequence numbers: after AppSearch (50) / LaunchConditions (100) which populate the property,
    // and strictly below InstallInitialize (1500) which is where the install begins committing.
    private const int FirstEvalSequence = 101;

    // Every eval/abort pair must land below this ceiling so the abort can never be scheduled at or
    // past InstallInitialize (which would let the install commit before the gate fires). The offset
    // wraps within [FirstEvalSequence, SequenceCeiling) for pathological consumer counts; since the
    // Action column is the primary key, a wrapped (duplicate) Sequence only makes ordering among
    // checks ambiguous — every check still runs before commit, preserving the safety guarantee.
    private const int SequenceCeiling = 1400;

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

            // Two consecutive slots per check, wrapped below SequenceCeiling to guarantee the
            // before-commit invariant regardless of consumer count.
            var span = SequenceCeiling - FirstEvalSequence - 1; // even count of usable slots
            var evalSeq = FirstEvalSequence + ((index * 2) % span);
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
    ///     extension instances. Shared with <c>DotNetSearchPlanner.Suffix</c> via
    ///     <see cref="MsiSearchNaming.Suffix"/>.
    /// </summary>
    private static string Suffix(DependencyConsumerModel consumer)
        => MsiSearchNaming.Suffix($"{consumer.ProviderKey} {consumer.ConsumerKey}");

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
        // MSI Formatted-text escaping so an authored key containing '[' or ']' cannot inject a
        // spurious [Property] token. CommandLine.MsiFormatEscape is single-pass — a naive
        // two-Replace escaper re-mangles the brackets it just introduced.
        var providerKey = CommandLine.MsiFormatEscape(consumer.ProviderKey);
        var consumerKey = CommandLine.MsiFormatEscape(consumer.ConsumerKey);

        // The range text is deliberately bracket-free (worded, not interval notation): a literal
        // '[' would be parsed as the start of a Formatted property token and would swallow the
        // '[{property}]' detected-version substitution that follows.
        var rangeText = FormatRangeText(range);

        var message =
            $"Dependency '{providerKey}' (required by '{consumerKey}') must be installed with a version " +
            $"{rangeText}; the detected version was '[{property}]' (blank means not installed). " +
            "Install or upgrade the required provider, then run this installer again.";

        return message.Length <= MaxMessageLength
            ? message
            : message[..(MaxMessageLength - 1)] + ".";
    }

    /// <summary>
    ///     Normalizes a <see cref="Version"/> to a full four-part string (missing Build/Revision
    ///     become 0) so the JScript comparison operands are unambiguous. Shared with
    ///     <c>DotNetSearchPlanner.FormatVersion</c> via <see cref="MsiSearchNaming.FormatVersion"/>.
    /// </summary>
    private static string FormatVersion(Version version)
        => MsiSearchNaming.FormatVersion(version);

    /// <summary>
    ///     Renders the effective range as bracket-free English (never MSI interval notation, whose
    ///     <c>[</c>/<c>)</c> characters would be mis-parsed as Formatted property tokens).
    /// </summary>
    private static string FormatRangeText(VersionRange range)
    {
        var minPhrase = BoundPhrase(range.MinVersion, range.MinInclusive, "at least ", "greater than ");
        var maxPhrase = BoundPhrase(range.MaxVersion, range.MaxInclusive, "at most ", "less than ");

        if (minPhrase is not null && maxPhrase is not null)
            return $"{minPhrase} and {maxPhrase}";

        // Exactly one bound is present here — Plan() skips consumers with both bounds null.
        return minPhrase ?? maxPhrase!;
    }

    private static string? BoundPhrase(Version? bound, bool inclusive, string inclusiveWord, string exclusiveWord)
    {
        if (bound is null)
            return null;

        var word = inclusive ? inclusiveWord : exclusiveWord;
        return word + FormatVersion(bound);
    }
}
