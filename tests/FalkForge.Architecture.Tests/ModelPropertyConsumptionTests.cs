using System.Collections.Frozen;
using System.Reflection;
using System.Text;
using FalkForge.Compiler.Bundle;
using FalkForge.Compiler.Bundle.Compilation;
using FalkForge.Compiler.Msi;
using FalkForge.Compiler.Msix;
using FalkForge.Models;
using Xunit;

namespace FalkForge.Architecture.Tests;

/// <summary>
/// Guards against a defect class this codebase has shipped four times: a public property on a
/// compiler-input model that the fluent API happily accepts and no compiler ever reads. The
/// caller configures it, no error is raised, and the setting silently does nothing —
/// <c>TransformBuilder.SetProperty</c>, <c>MsiCompiler(IFileSystem)</c>,
/// <c>MergeModuleBuilder.Dependency</c> and <c>MsixBuilder.Extension</c> were all found this way,
/// each only because somebody happened to write a round-trip test for that one compiler.
/// </summary>
/// <remarks>
/// <para><b>How it works.</b> For every guarded model type, every public instance property must
/// have its getter called from the IL of at least one compiler assembly. Reads are detected in
/// compiled IL (see <see cref="PropertyGetterScanner"/>), not in source text, because names
/// repeat across models: a grep for <c>.Scope</c> finds <c>BundleModel.Scope</c> and concludes
/// <c>MsixModel.Scope</c> is used. It also means the pervasive fluent-lambda style
/// (<c>p.Registry(r =&gt; ...)</c>) cannot produce a false pass — configuring a builder is not
/// reading a model.</para>
/// <para><b>What counts as a consumer.</b> Only the production assemblies in
/// <see cref="ConsumerAssemblyMarkers"/> — the model's own assembly plus the compilers that turn
/// models into artifacts. Test assemblies are deliberately excluded: if asserting on a property
/// counted as consuming it, adding a test would silence the guard for the very property the test
/// proves nothing about. The CLI, Studio and the decompiler are excluded for the same reason —
/// they populate models, and a property only they touch still never reaches an output file.</para>
/// <para><b>Known blind spot.</b> A property consumed purely by reflection would be reported as
/// unread. The guarded assemblies are NativeAOT-constrained and reflection-free over these
/// models; should that ever change, the property belongs in <see cref="Waivers"/> with that
/// reason.</para>
/// </remarks>
public sealed class ModelPropertyConsumptionTests
{
    /// <summary>
    /// The compiler-input models: the root object each compiler is handed. Their nested element
    /// types (<c>FileEntryModel</c>, <c>ServiceModel</c>, …) are out of scope for now — they are
    /// reached through these roots, and covering the full graph is a larger, noisier sweep that
    /// deserves its own pass rather than being bolted on here.
    /// </summary>
    private static readonly Type[] GuardedModels =
    [
        typeof(PackageModel),
        typeof(TransformModel),
        typeof(MergeModuleModel),
        typeof(PatchModel),
        typeof(BundleModel),
        typeof(MsixModel),
        typeof(MsixApplication),
        typeof(MsixBundleModel)
    ];

    /// <summary>One type per assembly whose IL is searched for reads.</summary>
    private static readonly Type[] ConsumerAssemblyMarkers =
    [
        typeof(PackageModel),    // FalkForge.Core — builders, validators, resolvers
        typeof(MsiCompiler),     // FalkForge.Compiler.Msi
        typeof(MsixCompiler),    // FalkForge.Compiler.Msix
        typeof(BundleCompiler)   // FalkForge.Compiler.Bundle
    ];

    /// <summary>
    /// Properties known to have no consumer, each with the reason. An entry here is a documented
    /// exception, not a mute button: <see cref="EveryWaiverIsStillEarned"/> fails as loudly as a
    /// missing consumer would if the property starts being read, or stops existing, so the list
    /// cannot quietly rot into a list of things nobody remembers.
    /// </summary>
    /// <remarks>
    /// Everything currently listed is an <em>unfixed instance of the guarded defect class</em>,
    /// not a false positive — they are the guard's first catch, recorded so they are visible
    /// rather than silent. Fixing them means either wiring the property into the compiler that
    /// should honour it or deleting it, and each is a change to a different compiler with its own
    /// blast radius, so they are left for follow-up rather than bundled into the commit that
    /// introduces the guard.
    /// </remarks>
    private static readonly FrozenDictionary<string, string> Waivers =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FalkForge.Models.PackageModel.Directories"] =
                "Deliberate, and documented on DirectoryTableProducer: directory rows are derived " +
                "from file targets rather than authored, so the recipe pipeline never reads this " +
                "list. It should be deleted from the model; that is a public API change to the " +
                "busiest model in the codebase and needs its own pass.",

            ["FalkForge.Models.PackageModel.CabinetThreadCount"] =
                "PackageBuilder.CabinetThreadCount is accepted and stored, and nothing in the " +
                "cabinet writer reads it — compression is single-threaded regardless of what the " +
                "caller asks for. Needs either real threading in the FCI path or removal.",

            ["FalkForge.Models.PatchModel.TargetProductCode"] =
                "PatchBuilder.TargetProductCode is accepted and stored; PatchCompiler writes the " +
                "patch summary information without it, so the patch is not bound to the product " +
                "the caller named. Wiring it means deciding how it maps onto the MSP summary " +
                "Template/TargetProductCodes fields.",

            ["FalkForge.Models.TransformModel.Description"] =
                "TransformBuilder.Description is accepted and stored; the transform compiler never " +
                "writes it into the .mst summary information, unlike its MSM and MSP siblings " +
                "which both do write theirs."
        }.ToFrozenDictionary(StringComparer.Ordinal);

    [Fact]
    public void EveryModelPropertyIsReadBySomeCompiler()
    {
        var unread = FindUnreadProperties();
        var unwaived = unread.Where(key => !Waivers.ContainsKey(key)).ToList();

        Assert.True(unwaived.Count == 0, BuildFailureMessage(unwaived));
    }

    [Fact]
    public void EveryWaiverIsStillEarned()
    {
        var unread = FindUnreadProperties().ToHashSet(StringComparer.Ordinal);
        var stale = new List<string>();

        foreach (var (key, reason) in Waivers)
        {
            if (string.IsNullOrWhiteSpace(reason))
                stale.Add($"{key}: waived without a reason.");
            else if (!unread.Contains(key))
                stale.Add($"{key}: now read by a compiler (or no longer exists) — delete the waiver.");
        }

        Assert.True(stale.Count == 0,
            "The waiver list has drifted from reality:" + Environment.NewLine +
            string.Join(Environment.NewLine, stale));
    }

    private static List<string> FindUnreadProperties()
    {
        var guardedTypeNames = GuardedModels
            .Select(t => t.FullName!)
            .ToFrozenSet(StringComparer.Ordinal);

        var reads = new HashSet<(string Type, string Property)>();
        foreach (var marker in ConsumerAssemblyMarkers)
        {
            var path = marker.Assembly.Location;
            Assert.True(File.Exists(path), $"Consumer assembly not found on disk: {path}");
            reads.UnionWith(PropertyGetterScanner.FindGetterCalls(path, guardedTypeNames));
        }

        var unread = new List<string>();
        foreach (var model in GuardedModels)
        {
            foreach (var property in model.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (property.GetMethod is null)
                    continue;

                if (!reads.Contains((model.FullName!, property.Name)))
                    unread.Add($"{model.FullName}.{property.Name}");
            }
        }

        unread.Sort(StringComparer.Ordinal);
        return unread;
    }

    private static string BuildFailureMessage(List<string> unwaived)
    {
        var message = new StringBuilder()
            .AppendLine("These model properties are accepted by the public API but no compiler ever reads them,")
            .AppendLine("so setting them does nothing and the caller is never told:")
            .AppendLine();

        foreach (var property in unwaived)
            message.Append("  - ").AppendLine(property);

        return message
            .AppendLine()
            .AppendLine("Wire the property into the compiler that should honour it, remove it from the model,")
            .AppendLine("or — if it genuinely has no consumer yet — add it to the Waivers list with the reason.")
            .ToString();
    }
}
