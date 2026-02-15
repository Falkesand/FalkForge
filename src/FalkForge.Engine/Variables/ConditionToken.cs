namespace FalkForge.Engine.Variables;

public readonly record struct ConditionToken(TokenType Type, string Value)
{
    public static ConditionToken End() => new(TokenType.End, string.Empty);
    public static ConditionToken Invalid(string value) => new(TokenType.Invalid, value);
}
