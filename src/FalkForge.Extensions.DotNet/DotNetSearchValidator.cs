using System.Text.RegularExpressions;

namespace FalkForge.Extensions.DotNet;

public static partial class DotNetSearchValidator
{
    public static Result<Unit> Validate(DotNetCoreSearchModel model)
    {
        if (string.IsNullOrWhiteSpace(model.VariableName))
            return Result<Unit>.Failure(ErrorKind.Validation, "NET001: VariableName is required.");

        if (model.MinimumVersion is null)
            return Result<Unit>.Failure(ErrorKind.Validation, "NET002: MinimumVersion is required.");

        if (!MsiIdentifierPattern().IsMatch(model.VariableName))
            return Result<Unit>.Failure(ErrorKind.Validation,
                $"NET005: variable name '{model.VariableName}' is not a valid PUBLIC MSI property identifier " +
                "(must match [A-Z_][A-Z0-9_.]*, ALL UPPERCASE — e.g. DOTNET8_FOUND). Per the Windows Installer " +
                "SDK, AppSearch.Property must be a PUBLIC property (an identifier with no lowercase letters) to " +
                "be reliably populated by the built-in AppSearch standard action — a private/lowercase property " +
                "name is not guaranteed to survive that gate. The name also flows verbatim into a " +
                "LaunchCondition expression — an illegal name (e.g. containing spaces or operators) can be " +
                "evaluated by msiexec as an always-true expression, silently defeating the install gate.");

        switch (model.RuntimeType)
        {
            case DotNetRuntimeType.Runtime:
            case DotNetRuntimeType.AspNetCore:
            case DotNetRuntimeType.WindowsDesktop:
                break;
            case DotNetRuntimeType.Sdk:
                return Result<Unit>.Failure(ErrorKind.Validation,
                    "NET004: RuntimeType 'Sdk' is not supported for MSI-native detection — the SDK has no " +
                    "shared-framework directory to search (it is versioned via dotnet\\sdk\\{version}\\, a " +
                    "different layout). Search for Runtime, AspNetCore, or WindowsDesktop instead.");
            default:
                // An out-of-range enum value (e.g. an unchecked cast) has no shared-framework sentinel
                // either — DotNetSearchPlanner.SharedFrameworkInfo's default arm is a defensive throw,
                // not a Result-style failure, so this must be caught here before a plan is ever built.
                return Result<Unit>.Failure(ErrorKind.Validation,
                    $"NET004: RuntimeType '{model.RuntimeType}' is not a recognized value. Supported values: " +
                    "Runtime, AspNetCore, WindowsDesktop.");
        }

        if (model.Platform is not (DotNetPlatform.X64 or DotNetPlatform.X86 or DotNetPlatform.Arm64))
            return Result<Unit>.Failure(ErrorKind.Validation,
                $"NET007: Platform '{model.Platform}' is not a recognized value. Supported values: X64, X86, " +
                "Arm64.");

        return Unit.Value;
    }

    public static Result<Unit> ValidateAll(IReadOnlyList<DotNetCoreSearchModel> models)
    {
        var variables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in models)
        {
            var result = Validate(model);
            if (result.IsFailure)
                return result;

            if (!string.IsNullOrWhiteSpace(model.VariableName) && !variables.Add(model.VariableName))
                return Result<Unit>.Failure(ErrorKind.Validation,
                    $"NET003: Duplicate VariableName '{model.VariableName}'.");
        }

        return Unit.Value;
    }

    /// <summary>
    ///     Legal identifier grammar for a PUBLIC MSI <c>Property</c> name (per the MSI SDK's
    ///     Identifier data type, narrowed to the public-property subset): starts with an uppercase
    ///     letter or underscore, followed by uppercase letters/digits/underscore/period — no
    ///     lowercase letters anywhere. Anything else — a leading digit, whitespace, operators
    ///     (<c>OR</c>, <c>=</c>, etc.), or any lowercase letter — either fails <c>msi.dll</c> table
    ///     insertion outright, is silently accepted as a <c>LaunchCondition</c> EXPRESSION rather
    ///     than a property reference (e.g. <c>"1 OR 1"</c> evaluates to always-true), or (lowercase)
    ///     produces a PRIVATE property that the built-in <c>AppSearch</c> standard action is not
    ///     reliably guaranteed to populate — each defeating the install gate without any build-time
    ///     error unless rejected here.
    /// </summary>
    [GeneratedRegex(@"^[A-Z_][A-Z0-9_.]*$")]
    private static partial Regex MsiIdentifierPattern();
}