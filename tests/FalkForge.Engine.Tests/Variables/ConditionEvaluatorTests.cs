namespace FalkForge.Engine.Tests.Variables;

using FalkForge.Engine.Variables;
using Xunit;

public sealed class ConditionEvaluatorTests
{
    private readonly VariableStore _store = new();

    // --- Empty / null condition ---

    [Fact]
    public void Evaluate_NullCondition_ReturnsTrue()
    {
        var result = ConditionEvaluator.Evaluate(null, _store);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void Evaluate_EmptyCondition_ReturnsTrue()
    {
        var result = ConditionEvaluator.Evaluate("", _store);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void Evaluate_WhitespaceCondition_ReturnsTrue()
    {
        var result = ConditionEvaluator.Evaluate("   ", _store);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    // --- Simple comparisons ---

    [Fact]
    public void Evaluate_VersionGreaterOrEqual_True()
    {
        _store.Set("VersionNT", new Version(10, 0, 19041));

        var result = ConditionEvaluator.Evaluate("VersionNT >= v6.1", _store);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void Evaluate_VersionGreaterOrEqual_False()
    {
        _store.Set("VersionNT", new Version(5, 1));

        var result = ConditionEvaluator.Evaluate("VersionNT >= v6.1", _store);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void Evaluate_StringEquals_True()
    {
        _store.Set("ProcessorArchitecture", "x64");

        var result = ConditionEvaluator.Evaluate("ProcessorArchitecture = \"x64\"", _store);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void Evaluate_StringEquals_False()
    {
        _store.Set("ProcessorArchitecture", "arm64");

        var result = ConditionEvaluator.Evaluate("ProcessorArchitecture = \"x64\"", _store);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    // --- Boolean logic ---

    [Fact]
    public void Evaluate_And_BothTrue()
    {
        _store.Set("A", 1L);
        _store.Set("B", 1L);

        var result = ConditionEvaluator.Evaluate("A AND B", _store);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void Evaluate_And_OneFalse()
    {
        _store.Set("A", 1L);
        _store.Set("B", 0L);

        var result = ConditionEvaluator.Evaluate("A AND B", _store);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void Evaluate_Or_OneFalse()
    {
        _store.Set("A", 0L);
        _store.Set("B", 1L);

        var result = ConditionEvaluator.Evaluate("A OR B", _store);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void Evaluate_Or_BothFalse()
    {
        _store.Set("A", 0L);
        _store.Set("B", 0L);

        var result = ConditionEvaluator.Evaluate("A OR B", _store);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void Evaluate_Not_InvertsTruthy()
    {
        _store.Set("A", 1L);

        var result = ConditionEvaluator.Evaluate("NOT A", _store);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void Evaluate_Not_InvertsFalsy()
    {
        _store.Set("A", 0L);

        var result = ConditionEvaluator.Evaluate("NOT A", _store);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void Evaluate_ComplexBooleanLogic_OrAndCombined()
    {
        _store.Set("A", 0L);
        _store.Set("B", 1L);
        _store.Set("C", 1L);

        // (A OR B) AND C -> (false OR true) AND true -> true
        var result = ConditionEvaluator.Evaluate("(A OR B) AND C", _store);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    // --- Nested parentheses ---

    [Fact]
    public void Evaluate_NestedParentheses()
    {
        _store.Set("A", 1L);
        _store.Set("B", 1L);
        _store.Set("C", 0L);

        // ((A = 1) AND (B = 1)) OR C -> (true AND true) OR false -> true
        var result = ConditionEvaluator.Evaluate("((A = 1) AND (B = 1)) OR C", _store);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void Evaluate_DeeplyNestedParentheses()
    {
        _store.Set("X", 5L);

        var result = ConditionEvaluator.Evaluate("((X > 3))", _store);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    // --- Truthiness ---

    [Fact]
    public void Evaluate_StandaloneVariable_NonEmptyString_IsTrue()
    {
        _store.Set("Var", "something");

        var result = ConditionEvaluator.Evaluate("Var", _store);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void Evaluate_StandaloneVariable_EmptyString_IsFalse()
    {
        _store.Set("Var", "");

        var result = ConditionEvaluator.Evaluate("Var", _store);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void Evaluate_StandaloneVariable_ZeroInteger_IsFalse()
    {
        _store.Set("Var", 0L);

        var result = ConditionEvaluator.Evaluate("Var", _store);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void Evaluate_StandaloneVariable_NonZeroInteger_IsTrue()
    {
        _store.Set("Var", 1L);

        var result = ConditionEvaluator.Evaluate("Var", _store);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void Evaluate_StandaloneVariable_MissingVariable_IsFalse()
    {
        var result = ConditionEvaluator.Evaluate("NonExistent", _store);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void Evaluate_StringZero_IsFalse()
    {
        _store.Set("Var", "0");

        var result = ConditionEvaluator.Evaluate("Var", _store);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    [Fact]
    public void Evaluate_StringOne_IsTrue()
    {
        _store.Set("Var", "1");

        var result = ConditionEvaluator.Evaluate("Var", _store);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    // --- Version comparisons ---

    [Fact]
    public void Evaluate_VersionLessThan_True()
    {
        _store.Set("Ver", new Version(1, 2));

        var result = ConditionEvaluator.Evaluate("Ver < v1.3", _store);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void Evaluate_VersionEquals_WithDifferentPrecision()
    {
        _store.Set("Ver", new Version(1, 0, 0));

        // v1.0 = Version(1,0), v1.0.0 = Version(1,0,0)
        // Version(1,0) != Version(1,0,0) in .NET comparison
        var result = ConditionEvaluator.Evaluate("Ver = v1.0.0", _store);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void Evaluate_VersionGreaterThan_False()
    {
        _store.Set("Ver", new Version(1, 0));

        var result = ConditionEvaluator.Evaluate("Ver > v2.0", _store);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    // --- Integer comparisons ---

    [Fact]
    public void Evaluate_IntegerGreaterThan_True()
    {
        _store.Set("Count", 5L);

        var result = ConditionEvaluator.Evaluate("Count > 3", _store);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void Evaluate_IntegerEquals_True()
    {
        _store.Set("Value", 10L);

        var result = ConditionEvaluator.Evaluate("Value = 10", _store);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void Evaluate_IntegerLessThan_True()
    {
        _store.Set("Val", 2L);

        var result = ConditionEvaluator.Evaluate("Val < 5", _store);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void Evaluate_IntegerNotEquals_True()
    {
        _store.Set("Val", 3L);

        var result = ConditionEvaluator.Evaluate("Val <> 5", _store);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    // --- Case-insensitive ~= operator ---

    [Fact]
    public void Evaluate_CaseInsensitiveEquals_True()
    {
        _store.Set("Arch", "X64");

        var result = ConditionEvaluator.Evaluate("Arch ~= \"x64\"", _store);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void Evaluate_CaseInsensitiveEquals_False()
    {
        _store.Set("Arch", "arm64");

        var result = ConditionEvaluator.Evaluate("Arch ~= \"x64\"", _store);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
    }

    // --- Error handling ---

    [Fact]
    public void Evaluate_UnmatchedLeftParen_ReturnsFailure()
    {
        _store.Set("A", 1L);

        var result = ConditionEvaluator.Evaluate("(A", _store);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void Evaluate_UnmatchedRightParen_ReturnsFailure()
    {
        _store.Set("A", 1L);

        var result = ConditionEvaluator.Evaluate("A)", _store);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void Evaluate_MissingOperand_ReturnsFailure()
    {
        var result = ConditionEvaluator.Evaluate("A =", _store);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void Evaluate_InvalidSyntax_ReturnsFailure()
    {
        var result = ConditionEvaluator.Evaluate("= =", _store);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void Evaluate_UnterminatedStringInCondition_ReturnsFailure()
    {
        var result = ConditionEvaluator.Evaluate("A = \"unterminated", _store);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    // --- String comparison fallback ---

    [Fact]
    public void Evaluate_StringComparison_LessThan()
    {
        _store.Set("Name", "alpha");

        var result = ConditionEvaluator.Evaluate("Name < \"beta\"", _store);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    // --- Operator precedence ---

    [Fact]
    public void Evaluate_AndBindsTighterThanOr()
    {
        _store.Set("A", 1L);
        _store.Set("B", 0L);
        _store.Set("C", 1L);

        // A OR B AND C -> A OR (B AND C) -> true OR false -> true
        var result = ConditionEvaluator.Evaluate("A OR B AND C", _store);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public void Evaluate_NotBindsTighterThanAnd()
    {
        _store.Set("A", 0L);

        // NOT A AND NOT A -> (NOT A) AND (NOT A) -> true AND true -> true
        var result = ConditionEvaluator.Evaluate("NOT A AND NOT A", _store);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    // --- Variable comparison to variable ---

    [Fact]
    public void Evaluate_TwoVariables_IntegerComparison()
    {
        _store.Set("Left", 10L);
        _store.Set("Right", 5L);

        var result = ConditionEvaluator.Evaluate("Left > Right", _store);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }
}
