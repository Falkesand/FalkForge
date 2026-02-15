namespace FalkInstaller.Engine.Variables;

/// <summary>
/// Recursive-descent evaluator for condition expressions.
/// Grammar:
///   expr     -> or_expr
///   or_expr  -> and_expr (OR and_expr)*
///   and_expr -> not_expr (AND not_expr)*
///   not_expr -> NOT not_expr | primary
///   primary  -> comparison | '(' expr ')'
///   comparison -> value (op value)?
///   value    -> variable | literal
///   op       -> '=' | '<>' | '<' | '>' | '<=' | '>=' | '~='
/// </summary>
public static class ConditionEvaluator
{
    public static Result<bool> Evaluate(string? condition, VariableStore variables)
    {
        // Empty/null condition is unconditionally true
        if (string.IsNullOrWhiteSpace(condition))
        {
            return true;
        }

        var tokenizeResult = ConditionLexer.Tokenize(condition);
        if (tokenizeResult.IsFailure)
        {
            return Result<bool>.Failure(tokenizeResult.Error);
        }

        var tokens = tokenizeResult.Value;
        var parser = new Parser(tokens, variables);

        var result = parser.ParseExpression();
        if (result.IsFailure)
        {
            return Result<bool>.Failure(result.Error);
        }

        // Ensure we consumed all tokens (should be at End)
        if (parser.Current.Type != TokenType.End)
        {
            return Result<bool>.Failure(ErrorKind.Validation,
                $"Unexpected token '{parser.Current.Value}' after expression");
        }

        return result;
    }

    private sealed class Parser
    {
        private readonly IReadOnlyList<ConditionToken> _tokens;
        private readonly VariableStore _variables;
        private int _pos;

        public Parser(IReadOnlyList<ConditionToken> tokens, VariableStore variables)
        {
            _tokens = tokens;
            _variables = variables;
        }

        public ConditionToken Current => _pos < _tokens.Count
            ? _tokens[_pos]
            : ConditionToken.End();

        private void Advance() => _pos++;

        public Result<bool> ParseExpression() => ParseOrExpr();

        private Result<bool> ParseOrExpr()
        {
            var left = ParseAndExpr();
            if (left.IsFailure) return left;

            while (Current.Type == TokenType.Or)
            {
                Advance();
                var right = ParseAndExpr();
                if (right.IsFailure) return right;
                left = left.Value || right.Value;
            }

            return left;
        }

        private Result<bool> ParseAndExpr()
        {
            var left = ParseNotExpr();
            if (left.IsFailure) return left;

            while (Current.Type == TokenType.And)
            {
                Advance();
                var right = ParseNotExpr();
                if (right.IsFailure) return right;
                left = left.Value && right.Value;
            }

            return left;
        }

        private Result<bool> ParseNotExpr()
        {
            if (Current.Type == TokenType.Not)
            {
                Advance();
                var inner = ParseNotExpr();
                if (inner.IsFailure) return inner;
                return !inner.Value;
            }

            return ParsePrimary();
        }

        private Result<bool> ParsePrimary()
        {
            if (Current.Type == TokenType.LeftParen)
            {
                Advance();
                var inner = ParseExpression();
                if (inner.IsFailure) return inner;

                if (Current.Type != TokenType.RightParen)
                {
                    return Result<bool>.Failure(ErrorKind.Validation, "Expected closing parenthesis");
                }
                Advance();
                return inner;
            }

            return ParseComparison();
        }

        private Result<bool> ParseComparison()
        {
            var leftResult = ParseValue();
            if (leftResult.IsFailure) return Result<bool>.Failure(leftResult.Error);
            var left = leftResult.Value;

            // Check for comparison operator
            if (IsComparisonOp(Current.Type))
            {
                var op = Current.Type;
                Advance();

                var rightResult = ParseValue();
                if (rightResult.IsFailure) return Result<bool>.Failure(rightResult.Error);
                var right = rightResult.Value;

                return Compare(left, op, right);
            }

            // Standalone value: evaluate truthiness
            return IsTruthy(left);
        }

        private Result<ConditionValue> ParseValue()
        {
            var token = Current;

            switch (token.Type)
            {
                case TokenType.Variable:
                    Advance();
                    return ResolveVariable(token.Value);

                case TokenType.StringLiteral:
                    Advance();
                    return new ConditionValue(token.Value, null, null);

                case TokenType.IntLiteral:
                    Advance();
                    if (long.TryParse(token.Value, out var intVal))
                    {
                        return new ConditionValue(token.Value, intVal, null);
                    }
                    return new ConditionValue(token.Value, null, null);

                case TokenType.VersionLiteral:
                    Advance();
                    if (Version.TryParse(token.Value, out var verVal))
                    {
                        return new ConditionValue(token.Value, null, verVal);
                    }
                    return new ConditionValue(token.Value, null, null);

                case TokenType.End:
                    return Result<ConditionValue>.Failure(ErrorKind.Validation,
                        "Unexpected end of expression");

                default:
                    return Result<ConditionValue>.Failure(ErrorKind.Validation,
                        $"Unexpected token '{token.Value}' (type {token.Type})");
            }
        }

        private ConditionValue ResolveVariable(string name)
        {
            var raw = _variables.GetRaw(name);
            if (raw is null)
            {
                return new ConditionValue(null, null, null);
            }

            return raw switch
            {
                string s => new ConditionValue(s,
                    long.TryParse(s, out var si) ? si : null,
                    Version.TryParse(s, out var sv) ? sv : null),
                long l => new ConditionValue(l.ToString(), l, null),
                Version v => new ConditionValue(v.ToString(), null, v),
                _ => new ConditionValue(raw.ToString(), null, null)
            };
        }

        private static Result<bool> Compare(ConditionValue left, TokenType op, ConditionValue right)
        {
            // ~= is always case-insensitive string compare
            if (op == TokenType.CaseInsensitiveEquals)
            {
                return string.Equals(
                    left.StringValue ?? string.Empty,
                    right.StringValue ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase);
            }

            // If both sides have Version, compare as Version
            if (left.VersionValue is not null && right.VersionValue is not null)
            {
                var cmp = left.VersionValue.CompareTo(right.VersionValue);
                return EvaluateComparison(cmp, op);
            }

            // If both sides have integer, compare as long
            if (left.IntValue is not null && right.IntValue is not null)
            {
                var cmp = left.IntValue.Value.CompareTo(right.IntValue.Value);
                return EvaluateComparison(cmp, op);
            }

            // Fallback: case-insensitive string comparison
            var strCmp = string.Compare(
                left.StringValue ?? string.Empty,
                right.StringValue ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);

            return EvaluateComparison(strCmp, op);
        }

        private static Result<bool> EvaluateComparison(int cmp, TokenType op)
        {
            return op switch
            {
                TokenType.Equals => cmp == 0,
                TokenType.NotEquals => cmp != 0,
                TokenType.LessThan => cmp < 0,
                TokenType.GreaterThan => cmp > 0,
                TokenType.LessOrEqual => cmp <= 0,
                TokenType.GreaterOrEqual => cmp >= 0,
                _ => Result<bool>.Failure(ErrorKind.Validation, $"Unknown comparison operator: {op}")
            };
        }

        private static bool IsTruthy(ConditionValue value)
        {
            // Null/missing variable is falsy
            if (value.StringValue is null)
            {
                return false;
            }

            // Empty string is falsy
            if (value.StringValue.Length == 0)
            {
                return false;
            }

            // Integer zero is falsy
            if (value.IntValue is 0)
            {
                return false;
            }

            return true;
        }

        private static bool IsComparisonOp(TokenType type) =>
            type is TokenType.Equals
                or TokenType.NotEquals
                or TokenType.LessThan
                or TokenType.GreaterThan
                or TokenType.LessOrEqual
                or TokenType.GreaterOrEqual
                or TokenType.CaseInsensitiveEquals;
    }

    /// <summary>
    /// Holds a resolved value with all its type interpretations.
    /// </summary>
    private readonly record struct ConditionValue(
        string? StringValue,
        long? IntValue,
        Version? VersionValue);
}
