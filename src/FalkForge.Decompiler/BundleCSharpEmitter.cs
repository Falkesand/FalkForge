using System.Globalization;
using System.Text;
using FalkForge.Compiler.Bundle;

namespace FalkForge.Decompiler;

/// <summary>
/// Converts a <see cref="BundleModel"/> into fluent C# source code
/// that recreates the bundle definition using the FalkForge BundleBuilder API.
/// </summary>
internal static class BundleCSharpEmitter
{
    public static string Emit(BundleModel bundle) =>
        Emit(bundle, preamble: null, unmappedFeatures: null);

    public static string Emit(
        BundleModel bundle,
        string? preamble = null,
        IReadOnlyList<WixUnmappedFeature>? unmappedFeatures = null)
    {
        var sb = new StringBuilder();
        var indent = 0;

        void AppendLine(string line = "")
        {
            if (string.IsNullOrEmpty(line))
            {
                sb.AppendLine();
                return;
            }

            sb.Append(new string(' ', indent * 4));
            sb.AppendLine(line);
        }

        if (preamble is not null)
        {
            EmitPreamble(preamble, AppendLine);
            AppendLine();
        }

        AppendLine($"// Decompiled from bundle: {bundle.Name}");
        AppendLine("// NOTE: Some information is lost during decompilation:");
        AppendLine("//   - UI configuration (logo, theme, watermark, banner) is not preserved");
        AppendLine("//   - Container download URLs are not preserved");
        AppendLine("//   - Custom UI project paths are not preserved");
        AppendLine();

        AppendLine("using FalkForge;");
        AppendLine("using FalkForge.Compiler.Bundle.Builders;");
        AppendLine();

        AppendLine("Installer.BuildBundle(b =>");
        AppendLine("{");
        indent++;

        AppendLine($"b.Name({Quote(bundle.Name)});");
        AppendLine($"b.Manufacturer({Quote(bundle.Manufacturer)});");
        AppendLine($"b.Version({Quote(bundle.Version)});");

        if (bundle.BundleId != Guid.Empty)
            AppendLine($"b.BundleId(new Guid({Quote(bundle.BundleId.ToString())}));");

        if (bundle.UpgradeCode != Guid.Empty)
            AppendLine($"b.UpgradeCode(new Guid({Quote(bundle.UpgradeCode.ToString())}));");

        if (bundle.Scope != InstallScope.PerMachine)
            AppendLine($"b.Scope(InstallScope.{bundle.Scope});");

        EmitUnmappedFeatures(unmappedFeatures, AppendLine);

        EmitRelatedBundles(bundle.RelatedBundles, AppendLine, ref indent);
        EmitContainers(bundle.Containers, AppendLine, ref indent);
        EmitUiConfig(bundle.UiConfig, AppendLine, ref indent);
        EmitChain(bundle.Chain, AppendLine, ref indent);

        indent--;
        AppendLine("});");

        return sb.ToString();
    }

    private static void EmitRelatedBundles(
        IReadOnlyList<RelatedBundleModel> relatedBundles,
        Action<string> appendLine,
        ref int indent)
    {
        if (relatedBundles.Count == 0)
            return;

        appendLine("");
        foreach (var rb in relatedBundles)
        {
            if (rb.Relation == RelatedBundleRelation.Upgrade)
            {
                appendLine($"b.RelatedBundle({Quote(rb.BundleId)});");
            }
            else
            {
                appendLine($"b.RelatedBundle({Quote(rb.BundleId)}, r => r.Relation(RelatedBundleRelation.{rb.Relation}));");
            }
        }
    }

    private static void EmitContainers(
        IReadOnlyList<ContainerModel> containers,
        Action<string> appendLine,
        ref int indent)
    {
        if (containers.Count == 0)
            return;

        appendLine("");
        foreach (var container in containers)
        {
            appendLine($"b.Container({Quote(container.Id)});");
        }
    }

    private static void EmitUiConfig(
        BundleUiConfig? uiConfig,
        Action<string> appendLine,
        ref int indent)
    {
        if (uiConfig is null)
            return;

        appendLine("");
        switch (uiConfig.UiType)
        {
            case BundleUiType.Silent:
                appendLine("b.UseSilentUI();");
                break;
            case BundleUiType.Custom:
                appendLine("// Custom UI project path not preserved during decompilation");
                appendLine("b.UseCustomUI(\"TODO: set UI project path\");");
                break;
            case BundleUiType.BuiltIn:
                EmitBuiltInUi(uiConfig, appendLine);
                break;
        }
    }

    private static void EmitBuiltInUi(BundleUiConfig uiConfig, Action<string> appendLine)
    {
        var args = new List<string>();

        if (uiConfig.LicenseFile is not null)
            args.Add($"licenseFile: {Quote(uiConfig.LicenseFile)}");

        if (args.Count == 0)
        {
            appendLine("b.UseBuiltInUI();");
        }
        else
        {
            appendLine($"b.UseBuiltInUI({string.Join(", ", args)});");
        }
    }

    private static void EmitChain(
        IReadOnlyList<ChainItem> chain,
        Action<string> appendLine,
        ref int indent)
    {
        if (chain.Count == 0)
            return;

        appendLine("");
        appendLine("b.Chain(c =>");
        appendLine("{");
        indent++;

        foreach (var item in chain)
        {
            switch (item)
            {
                case RollbackBoundaryChainItem rb:
                    EmitRollbackBoundary(rb.Boundary, appendLine, ref indent);
                    break;
                case PackageChainItem pkg:
                    EmitPackage(pkg.Package, appendLine, ref indent);
                    break;
            }
        }

        indent--;
        appendLine("});");
    }

    private static void EmitRollbackBoundary(
        RollbackBoundaryModel boundary,
        Action<string> appendLine,
        ref int indent)
    {
        if (!boundary.Vital)
        {
            appendLine($"c.RollbackBoundary({Quote(boundary.Id)}, rb => rb.Vital(false));");
        }
        else
        {
            appendLine($"c.RollbackBoundary({Quote(boundary.Id)});");
        }
    }

    private static void EmitPackage(
        BundlePackageModel package,
        Action<string> appendLine,
        ref int indent)
    {
        var methodName = package.Type switch
        {
            BundlePackageType.MsiPackage => "MsiPackage",
            BundlePackageType.ExePackage => "ExePackage",
            BundlePackageType.NetRuntime => "NetRuntime",
            BundlePackageType.MsuPackage => "MsuPackage",
            BundlePackageType.MspPackage => "MspPackage",
            BundlePackageType.BundlePackage => "BundlePackage",
            _ => "ExePackage"
        };

        var hasBody = HasPackageBody(package);
        if (!hasBody)
        {
            appendLine($"c.{methodName}({Quote(package.SourcePath)}, p => {{ }});");
            return;
        }

        appendLine($"c.{methodName}({Quote(package.SourcePath)}, p =>");
        appendLine("{");
        indent++;

        EmitPackageBody(package, appendLine, ref indent);

        indent--;
        appendLine("});");
    }

    private static bool HasPackageBody(BundlePackageModel package)
    {
        var defaultId = Path.GetFileNameWithoutExtension(package.SourcePath);
        if (package.Id != defaultId) return true;
        if (package.DisplayName != defaultId) return true;
        if (package.Version is not null) return true;
        if (!package.Vital) return true;
        if (package.InstallCondition is not null) return true;
        if (package.Properties.Count > 0) return true;
        if (package.ExitCodes.Count > 0) return true;
        if (package.RemotePayload is not null) return true;
        if (package.ContainerId is not null) return true;
        if (package.KbArticle is not null) return true;
        if (package.PatchCode is not null) return true;
        if (package.TargetProductCode is not null) return true;
        return false;
    }

    private static void EmitPackageBody(
        BundlePackageModel package,
        Action<string> appendLine,
        ref int indent)
    {
        var defaultId = Path.GetFileNameWithoutExtension(package.SourcePath);

        if (package.Id != defaultId)
            appendLine($"p.Id({Quote(package.Id)});");

        if (package.DisplayName != defaultId)
            appendLine($"p.DisplayName({Quote(package.DisplayName)});");

        if (package.Version is not null)
            appendLine($"p.Version({Quote(package.Version)});");

        if (!package.Vital)
            appendLine("p.Vital(false);");

        if (package.InstallCondition is not null)
            appendLine($"p.InstallCondition({Quote(package.InstallCondition)});");

        if (package.KbArticle is not null)
            appendLine($"p.KbArticle({Quote(package.KbArticle)});");

        if (package.PatchCode is not null)
            appendLine($"p.PatchCode({Quote(package.PatchCode)});");

        if (package.TargetProductCode is not null)
            appendLine($"p.TargetProductCode({Quote(package.TargetProductCode)});");

        if (package.ContainerId is not null)
            appendLine($"p.Container({Quote(package.ContainerId)});");

        if (package.RemotePayload is not null)
        {
            var rp = package.RemotePayload;
            appendLine($"p.RemotePayload({Quote(rp.DownloadUrl)}, {Quote(rp.Sha256Hash)}, {rp.Size.ToString(CultureInfo.InvariantCulture)});");
        }

        foreach (var exitCode in package.ExitCodes)
        {
            appendLine($"p.ExitCode({exitCode.Key.ToString(CultureInfo.InvariantCulture)}, ExitCodeBehavior.{exitCode.Value});");
        }

        foreach (var prop in package.Properties)
        {
            appendLine($"p.Property({Quote(prop.Key)}, {Quote(prop.Value)});");
        }
    }

    private static void EmitPreamble(string preamble, Action<string> appendLine)
    {
        appendLine("// ============================================================");
        foreach (var line in preamble.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            appendLine(string.IsNullOrEmpty(trimmed) ? "//" : $"// {trimmed}");
        }

        appendLine("// ============================================================");
    }

    private static void EmitUnmappedFeatures(
        IReadOnlyList<WixUnmappedFeature>? unmappedFeatures,
        Action<string> appendLine)
    {
        if (unmappedFeatures is null || unmappedFeatures.Count == 0)
            return;

        appendLine("");
        appendLine("// ============================================================");
        appendLine("// Unmapped WiX features (not supported by FalkForge)");
        appendLine("// Consider implementing these manually.");
        appendLine("// ============================================================");
        foreach (var feature in unmappedFeatures)
        {
            appendLine($"// [{feature.Category}] {feature.Description}");
        }
    }

    private static string Quote(string value)
    {
        var escaped = value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
        return $"\"{escaped}\"";
    }
}
