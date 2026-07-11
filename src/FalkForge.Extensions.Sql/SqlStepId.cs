using System.Text;

namespace FalkForge.Extensions.Sql;

/// <summary>
/// Derives stable, valid MSI identifiers for execution-step ids from author-supplied model ids,
/// mirroring <c>UtilStepId</c> / <c>FirewallCommandFactory.MakeStepId</c>. The identifier keys the
/// generated deferred custom actions (and their <c>_rb</c>/<c>_un</c> variants), so it must satisfy the
/// MSI identifier grammar (<c>^[A-Za-z_][A-Za-z0-9_]*$</c>) and stay well under the
/// <c>CustomAction.Action</c> CHAR(72) budget once the longest suffix is appended.
/// </summary>
internal static class SqlStepId
{
    private const int MaxBaseLength = 60;

    /// <summary>
    /// Builds a step id as <paramref name="prefix"/> + a sanitized copy of <paramref name="modelId"/>
    /// (non-identifier characters mapped to <c>_</c>), capped to <see cref="MaxBaseLength"/> characters.
    /// </summary>
    internal static string Make(string prefix, string modelId)
    {
        var sb = new StringBuilder(prefix, prefix.Length + modelId.Length);
        foreach (char c in modelId)
            sb.Append(char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_');

        return sb.Length > MaxBaseLength ? sb.ToString(0, MaxBaseLength) : sb.ToString();
    }
}
