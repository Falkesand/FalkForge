namespace FalkInstaller.Engine.Variables;

public static class ConditionLexer
{
    public static Result<IReadOnlyList<ConditionToken>> Tokenize(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return Result<IReadOnlyList<ConditionToken>>.Success([ConditionToken.End()]);
        }

        var tokens = new List<ConditionToken>();
        var span = expression.AsSpan();
        var pos = 0;

        while (pos < span.Length)
        {
            // Skip whitespace
            if (char.IsWhiteSpace(span[pos]))
            {
                pos++;
                continue;
            }

            var ch = span[pos];

            // Quoted string literal
            if (ch == '"')
            {
                var result = ReadStringLiteral(span, ref pos);
                if (result.IsFailure)
                {
                    return Result<IReadOnlyList<ConditionToken>>.Failure(result.Error);
                }
                tokens.Add(result.Value);
                continue;
            }

            // Parentheses
            if (ch == '(')
            {
                tokens.Add(new ConditionToken(TokenType.LeftParen, "("));
                pos++;
                continue;
            }
            if (ch == ')')
            {
                tokens.Add(new ConditionToken(TokenType.RightParen, ")"));
                pos++;
                continue;
            }

            // Operators: ~=, <=, >=, <>, <, >, =
            if (ch == '~' && pos + 1 < span.Length && span[pos + 1] == '=')
            {
                tokens.Add(new ConditionToken(TokenType.CaseInsensitiveEquals, "~="));
                pos += 2;
                continue;
            }
            if (ch == '<' && pos + 1 < span.Length && span[pos + 1] == '>')
            {
                tokens.Add(new ConditionToken(TokenType.NotEquals, "<>"));
                pos += 2;
                continue;
            }
            if (ch == '<' && pos + 1 < span.Length && span[pos + 1] == '=')
            {
                tokens.Add(new ConditionToken(TokenType.LessOrEqual, "<="));
                pos += 2;
                continue;
            }
            if (ch == '>' && pos + 1 < span.Length && span[pos + 1] == '=')
            {
                tokens.Add(new ConditionToken(TokenType.GreaterOrEqual, ">="));
                pos += 2;
                continue;
            }
            if (ch == '<')
            {
                tokens.Add(new ConditionToken(TokenType.LessThan, "<"));
                pos++;
                continue;
            }
            if (ch == '>')
            {
                tokens.Add(new ConditionToken(TokenType.GreaterThan, ">"));
                pos++;
                continue;
            }
            if (ch == '=')
            {
                tokens.Add(new ConditionToken(TokenType.Equals, "="));
                pos++;
                continue;
            }

            // Version literal: starts with 'v' followed by digit
            if (ch == 'v' && pos + 1 < span.Length && char.IsAsciiDigit(span[pos + 1]))
            {
                tokens.Add(ReadVersionLiteral(span, ref pos));
                continue;
            }

            // Integer literal: starts with digit
            if (char.IsAsciiDigit(ch))
            {
                tokens.Add(ReadNumberOrVersion(span, ref pos));
                continue;
            }

            // Identifier (variable name or keyword)
            if (IsIdentifierStart(ch))
            {
                tokens.Add(ReadIdentifierOrKeyword(span, ref pos));
                continue;
            }

            // Unknown character
            return Result<IReadOnlyList<ConditionToken>>.Failure(
                ErrorKind.Validation,
                $"Unexpected character '{ch}' at position {pos}");
        }

        tokens.Add(ConditionToken.End());
        return Result<IReadOnlyList<ConditionToken>>.Success(tokens);
    }

    private static Result<ConditionToken> ReadStringLiteral(ReadOnlySpan<char> span, ref int pos)
    {
        // Skip opening quote
        pos++;
        var start = pos;

        while (pos < span.Length && span[pos] != '"')
        {
            pos++;
        }

        if (pos >= span.Length)
        {
            return Result<ConditionToken>.Failure(ErrorKind.Validation, "Unterminated string literal");
        }

        var value = span[start..pos].ToString();
        pos++; // Skip closing quote
        return new ConditionToken(TokenType.StringLiteral, value);
    }

    private static ConditionToken ReadVersionLiteral(ReadOnlySpan<char> span, ref int pos)
    {
        // Skip 'v' prefix
        pos++;
        var start = pos;

        while (pos < span.Length && (char.IsAsciiDigit(span[pos]) || span[pos] == '.'))
        {
            pos++;
        }

        var versionStr = span[start..pos].ToString();
        return new ConditionToken(TokenType.VersionLiteral, versionStr);
    }

    private static ConditionToken ReadNumberOrVersion(ReadOnlySpan<char> span, ref int pos)
    {
        var start = pos;
        var hasDot = false;

        while (pos < span.Length && (char.IsAsciiDigit(span[pos]) || span[pos] == '.'))
        {
            if (span[pos] == '.')
            {
                hasDot = true;
            }
            pos++;
        }

        var text = span[start..pos].ToString();

        // If it contains dots, it's a version literal (e.g., 6.1, 1.0.0.0)
        if (hasDot && Version.TryParse(text, out _))
        {
            return new ConditionToken(TokenType.VersionLiteral, text);
        }

        // Otherwise it's an integer
        return new ConditionToken(TokenType.IntLiteral, text);
    }

    private static ConditionToken ReadIdentifierOrKeyword(ReadOnlySpan<char> span, ref int pos)
    {
        var start = pos;

        while (pos < span.Length && IsIdentifierChar(span[pos]))
        {
            pos++;
        }

        var text = span[start..pos].ToString();

        // Check for keywords (case-insensitive)
        if (text.Equals("AND", StringComparison.OrdinalIgnoreCase))
        {
            return new ConditionToken(TokenType.And, text);
        }
        if (text.Equals("OR", StringComparison.OrdinalIgnoreCase))
        {
            return new ConditionToken(TokenType.Or, text);
        }
        if (text.Equals("NOT", StringComparison.OrdinalIgnoreCase))
        {
            return new ConditionToken(TokenType.Not, text);
        }

        return new ConditionToken(TokenType.Variable, text);
    }

    private static bool IsIdentifierStart(char ch) =>
        char.IsAsciiLetter(ch) || ch == '_';

    private static bool IsIdentifierChar(char ch) =>
        char.IsAsciiLetterOrDigit(ch) || ch == '_' || ch == '.';
}
