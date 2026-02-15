namespace FalkForge.Engine.Tests.Variables;

using FalkForge.Engine.Variables;
using Xunit;

public sealed class ConditionLexerTests
{
    [Fact]
    public void Tokenize_EmptyString_ReturnsEndToken()
    {
        var result = ConditionLexer.Tokenize("");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal(TokenType.End, result.Value[0].Type);
    }

    [Fact]
    public void Tokenize_NullString_ReturnsEndToken()
    {
        var result = ConditionLexer.Tokenize(null);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal(TokenType.End, result.Value[0].Type);
    }

    [Fact]
    public void Tokenize_VariableName_ReturnsVariableToken()
    {
        var result = ConditionLexer.Tokenize("VersionNT");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count); // Variable + End
        Assert.Equal(TokenType.Variable, result.Value[0].Type);
        Assert.Equal("VersionNT", result.Value[0].Value);
    }

    [Fact]
    public void Tokenize_StringLiteral_ReturnsStringToken()
    {
        var result = ConditionLexer.Tokenize("\"hello world\"");

        Assert.True(result.IsSuccess);
        Assert.Equal(TokenType.StringLiteral, result.Value[0].Type);
        Assert.Equal("hello world", result.Value[0].Value);
    }

    [Fact]
    public void Tokenize_IntegerLiteral_ReturnsIntToken()
    {
        var result = ConditionLexer.Tokenize("42");

        Assert.True(result.IsSuccess);
        Assert.Equal(TokenType.IntLiteral, result.Value[0].Type);
        Assert.Equal("42", result.Value[0].Value);
    }

    [Fact]
    public void Tokenize_VersionLiteralWithPrefix_ReturnsVersionToken()
    {
        var result = ConditionLexer.Tokenize("v6.1.0");

        Assert.True(result.IsSuccess);
        Assert.Equal(TokenType.VersionLiteral, result.Value[0].Type);
        Assert.Equal("6.1.0", result.Value[0].Value);
    }

    [Fact]
    public void Tokenize_VersionLiteralWithoutPrefix_ReturnsVersionToken()
    {
        var result = ConditionLexer.Tokenize("1.2.3.4");

        Assert.True(result.IsSuccess);
        Assert.Equal(TokenType.VersionLiteral, result.Value[0].Type);
        Assert.Equal("1.2.3.4", result.Value[0].Value);
    }

    [Theory]
    [InlineData("=", TokenType.Equals)]
    [InlineData("<>", TokenType.NotEquals)]
    [InlineData("<", TokenType.LessThan)]
    [InlineData(">", TokenType.GreaterThan)]
    [InlineData("<=", TokenType.LessOrEqual)]
    [InlineData(">=", TokenType.GreaterOrEqual)]
    [InlineData("~=", TokenType.CaseInsensitiveEquals)]
    public void Tokenize_Operators_ReturnsCorrectTokenType(string op, TokenType expected)
    {
        var result = ConditionLexer.Tokenize($"A {op} B");

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value[1].Type);
    }

    [Fact]
    public void Tokenize_Parentheses_ReturnsCorrectTokens()
    {
        var result = ConditionLexer.Tokenize("(A)");

        Assert.True(result.IsSuccess);
        Assert.Equal(TokenType.LeftParen, result.Value[0].Type);
        Assert.Equal(TokenType.Variable, result.Value[1].Type);
        Assert.Equal(TokenType.RightParen, result.Value[2].Type);
    }

    [Theory]
    [InlineData("AND", TokenType.And)]
    [InlineData("OR", TokenType.Or)]
    [InlineData("NOT", TokenType.Not)]
    [InlineData("and", TokenType.And)]
    [InlineData("or", TokenType.Or)]
    [InlineData("not", TokenType.Not)]
    public void Tokenize_Keywords_ReturnsCorrectTokenType(string keyword, TokenType expected)
    {
        var result = ConditionLexer.Tokenize(keyword);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value[0].Type);
    }

    [Fact]
    public void Tokenize_ComplexExpression_ReturnsAllTokens()
    {
        var result = ConditionLexer.Tokenize("VersionNT >= v6.1 AND ProcessorArchitecture = \"x64\"");

        Assert.True(result.IsSuccess);
        // VersionNT, >=, v6.1, AND, ProcessorArchitecture, =, "x64", End
        Assert.Equal(8, result.Value.Count);
        Assert.Equal(TokenType.Variable, result.Value[0].Type);
        Assert.Equal(TokenType.GreaterOrEqual, result.Value[1].Type);
        Assert.Equal(TokenType.VersionLiteral, result.Value[2].Type);
        Assert.Equal(TokenType.And, result.Value[3].Type);
        Assert.Equal(TokenType.Variable, result.Value[4].Type);
        Assert.Equal(TokenType.Equals, result.Value[5].Type);
        Assert.Equal(TokenType.StringLiteral, result.Value[6].Type);
        Assert.Equal(TokenType.End, result.Value[7].Type);
    }

    [Fact]
    public void Tokenize_UnterminatedString_ReturnsFailure()
    {
        var result = ConditionLexer.Tokenize("\"unterminated");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void Tokenize_InvalidCharacter_ReturnsFailure()
    {
        var result = ConditionLexer.Tokenize("A @ B");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorKind.Validation, result.Error.Kind);
    }

    [Fact]
    public void Tokenize_VariableWithUnderscore_ReturnsVariable()
    {
        var result = ConditionLexer.Tokenize("InstalledVersion_Pkg1");

        Assert.True(result.IsSuccess);
        Assert.Equal(TokenType.Variable, result.Value[0].Type);
        Assert.Equal("InstalledVersion_Pkg1", result.Value[0].Value);
    }

    [Fact]
    public void Tokenize_VariableWithDot_ReturnsVariable()
    {
        var result = ConditionLexer.Tokenize("System.Feature");

        Assert.True(result.IsSuccess);
        Assert.Equal(TokenType.Variable, result.Value[0].Type);
        Assert.Equal("System.Feature", result.Value[0].Value);
    }
}
