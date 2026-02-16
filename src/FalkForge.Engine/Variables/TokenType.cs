namespace FalkForge.Engine.Variables;

public enum TokenType
{
    Variable,
    StringLiteral,
    IntLiteral,
    VersionLiteral,
    Equals,
    NotEquals,
    LessThan,
    GreaterThan,
    LessOrEqual,
    GreaterOrEqual,
    CaseInsensitiveEquals,
    And,
    Or,
    Not,
    LeftParen,
    RightParen,
    End,
    Invalid
}
