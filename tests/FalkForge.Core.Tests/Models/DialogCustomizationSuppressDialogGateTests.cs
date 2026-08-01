using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FalkForge.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace FalkForge.Core.Tests.Models;

/// <summary>
/// Proves that <see cref="DialogCustomization.SuppressDialog"/> is a genuine compile-time gate
/// (task #24), not merely a runtime-inert no-op or a downgradeable warning. A caller that writes
/// <c>new DialogCustomization().SuppressDialog(...)</c> must fail to compile with CS0619 (an
/// obsolete member marked as an error). Verified by actually compiling a snippet with Roslyn in
/// memory — the same technique <c>EmittedSourceCompilesTests</c> uses elsewhere in this repo —
/// rather than only inspecting the attribute, because an <c>[Obsolete]</c> without
/// <c>error: true</c> (or one silently removed in a future edit) would still let this pass while
/// leaving the API just as unusable-but-compilable as it was before task #24.
/// </summary>
public sealed class DialogCustomizationSuppressDialogGateTests
{
    private const string CallerSnippet = """
        using FalkForge.Models;

        internal static class Caller
        {
            internal static void UseIt()
            {
                var c = new DialogCustomization();
                c.SuppressDialog(StockDialog.License);
            }
        }
        """;

    [Fact]
    public void SuppressDialog_call_fails_to_compile_with_obsolete_error()
    {
        var tree = CSharpSyntaxTree.ParseText(CallerSnippet);
        var references = BuildMetadataReferences();

        var compilation = CSharpCompilation.Create(
            assemblyName: "SuppressDialogGateProbe",
            syntaxTrees: [tree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);

        Assert.False(
            result.Success,
            "DialogCustomization.SuppressDialog(...) compiled successfully — the "
            + "[Obsolete(error: true)] gate is missing, or was downgraded to a warning.");

        Assert.Contains(
            result.Diagnostics,
            d => d.Id == "CS0619" && d.Severity == DiagnosticSeverity.Error);
    }

    private static IReadOnlyList<MetadataReference> BuildMetadataReferences()
    {
        var references = new List<MetadataReference>();

        var trustedAssemblies =
            (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var path in trustedAssemblies)
        {
            references.Add(MetadataReference.CreateFromFile(path));
        }

        if (references.OfType<PortableExecutableReference>()
            .All(r => !string.Equals(r.FilePath, typeof(DialogCustomization).Assembly.Location, StringComparison.OrdinalIgnoreCase)))
        {
            references.Add(MetadataReference.CreateFromFile(typeof(DialogCustomization).Assembly.Location));
        }

        return references;
    }
}
