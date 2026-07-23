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
                $"NET005: variable name '{model.VariableName}' is not a valid MSI property identifier " +
                "(must match [A-Za-z_][A-Za-z0-9_.]*; use an UPPERCASE public property like " +
                "DOTNET8_FOUND so the gate is reliable). The name flows verbatim into a LaunchCondition " +
                "expression — an illegal name (e.g. containing spaces or operators) can be evaluated by " +
                "msiexec as an always-true expression, silently defeating the install gate.");

        if (model.RuntimeType == DotNetRuntimeType.Sdk)
            return Result<Unit>.Failure(ErrorKind.Validation,
                "NET004: RuntimeType 'Sdk' is not supported for MSI-native detection — the SDK has no " +
                "shared-framework directory to search (it is versioned via dotnet\\sdk\\{version}\\, a " +
                "different layout). Search for Runtime, AspNetCore, or WindowsDesktop instead.");

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
    ///     Legal MSI identifier grammar for a <c>Property</c> name (per the MSI SDK's Identifier data
    ///     type): starts with a letter or underscore, followed by letters/digits/underscore/period.
    ///     Anything else — leading digit, whitespace, operators (<c>OR</c>, <c>=</c>, etc.) — either
    ///     fails <c>msi.dll</c> table insertion outright or, worse, is silently accepted as a
    ///     <c>LaunchCondition</c> EXPRESSION rather than a property reference (e.g. <c>"1 OR 1"</c>
    ///     evaluates to always-true), defeating the install gate without any build-time error.
    /// </summary>
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.]*$")]
    private static partial Regex MsiIdentifierPattern();
}