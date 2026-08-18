using System.Globalization;
using System.Text;

namespace Haven.Application;

public sealed class DataFormulaParser
{
    private readonly List<Token> _tokens;
    private int _index;

    private DataFormulaParser(string? formula)
    {
        var source = formula?.Trim() ?? string.Empty;
        if (source.StartsWith('=') && source.Length > 0) source = source[1..];
        _tokens = Tokenize(source);
    }

    public static DataFormulaExpression Parse(string formula)
    {
        var parser = new DataFormulaParser(formula);
        var expression = parser.ParseComparison();
        parser.Expect(TokenKind.End);
        return expression;
    }

    private DataFormulaExpression ParseComparison()
    {
        var left = ParseConcat();
        while (Current.Kind is TokenKind.Equal or TokenKind.NotEqual or TokenKind.Less or TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual)
        {
            var kind = Consume().Kind;
            var right = ParseConcat();
            left = new DataFormulaBinaryExpression(kind switch
            {
                TokenKind.Equal => DataFormulaSegmentOperator.Equal,
                TokenKind.NotEqual => DataFormulaSegmentOperator.NotEqual,
                TokenKind.Less => DataFormulaSegmentOperator.Less,
                TokenKind.LessEqual => DataFormulaSegmentOperator.LessOrEqual,
                TokenKind.Greater => DataFormulaSegmentOperator.Greater,
                _ => DataFormulaSegmentOperator.GreaterOrEqual
            }, left, right);
        }
        return left;
    }

    private DataFormulaExpression ParseConcat()
    {
        var left = ParseAdditive();
        while (Match(TokenKind.Ampersand)) left = new DataFormulaBinaryExpression(DataFormulaSegmentOperator.Concat, left, ParseAdditive());
        return left;
    }

    private DataFormulaExpression ParseAdditive()
    {
        var left = ParseMultiplicative();
        while (Current.Kind is TokenKind.Plus or TokenKind.Minus)
        {
            var kind = Consume().Kind;
            left = new DataFormulaBinaryExpression(kind == TokenKind.Plus ? DataFormulaSegmentOperator.Add : DataFormulaSegmentOperator.Subtract, left, ParseMultiplicative());
        }
        return left;
    }

    private DataFormulaExpression ParseMultiplicative()
    {
        var left = ParsePower();
        while (Current.Kind is TokenKind.Star or TokenKind.Slash)
        {
            var kind = Consume().Kind;
            left = new DataFormulaBinaryExpression(kind == TokenKind.Star ? DataFormulaSegmentOperator.Multiply : DataFormulaSegmentOperator.Divide, left, ParsePower());
        }
        return left;
    }

    private DataFormulaExpression ParsePower()
    {
        var left = ParseUnary();
        if (Match(TokenKind.Caret)) left = new DataFormulaBinaryExpression(DataFormulaSegmentOperator.Power, left, ParsePower());
        return left;
    }

    private DataFormulaExpression ParseUnary()
    {
        if (Match(TokenKind.Plus)) return new DataFormulaUnaryExpression(DataFormulaUnaryOperator.Positive, ParseUnary());
        if (Match(TokenKind.Minus)) return new DataFormulaUnaryExpression(DataFormulaUnaryOperator.Negative, ParseUnary());
        var value = ParsePrimary();
        while (Match(TokenKind.Percent)) value = new DataFormulaUnaryExpression(DataFormulaUnaryOperator.Percent, value);
        return value;
    }

    private DataFormulaExpression ParsePrimary()
    {
        if (Match(TokenKind.LeftParen))
        {
            var expression = ParseComparison();
            Expect(TokenKind.RightParen);
            return expression;
        }
        if (Current.Kind == TokenKind.Number)
        {
            var token = Consume();
            if (!double.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) throw Error("Invalid number", token.Position);
            return new DataFormulaLiteralExpression(number);
        }
        if (Current.Kind == TokenKind.String) return new DataFormulaLiteralExpression(Consume().Text);
        if (Current.Kind != TokenKind.Identifier) throw Error("Expected a value, cell reference or function", Current.Position);

        var identifier = Consume();
        if (Match(TokenKind.LeftParen))
        {
            var arguments = new List<DataFormulaExpression>();
            if (!Match(TokenKind.RightParen))
            {
                do arguments.Add(ParseComparison()); while (Match(TokenKind.Comma));
                Expect(TokenKind.RightParen);
            }
            return new DataFormulaFunctionExpression(identifier.Text.ToUpperInvariant(), arguments);
        }

        if (Match(TokenKind.Bang))
        {
            var cell = Expect(TokenKind.Identifier);
            if (!DataFormulaReferenceUtility.TryParse(cell.Text, out var reference, identifier.Text)) throw Error("Expected an A1 reference after the sheet name", cell.Position);
            return ParseRangeTail(reference);
        }

        if (identifier.Text.Equals("#REF", StringComparison.OrdinalIgnoreCase) && Match(TokenKind.Bang)) return new DataFormulaErrorExpression(DataFormulaErrorCode.Reference);
        if (identifier.Text.Equals("TRUE", StringComparison.OrdinalIgnoreCase)) return new DataFormulaLiteralExpression(true);
        if (identifier.Text.Equals("FALSE", StringComparison.OrdinalIgnoreCase)) return new DataFormulaLiteralExpression(false);
        if (DataFormulaReferenceUtility.TryParse(identifier.Text, out var localReference)) return ParseRangeTail(localReference);
        return new DataFormulaNameExpression(identifier.Text);
    }

    private DataFormulaExpression ParseRangeTail(DataFormulaReference start)
    {
        if (!Match(TokenKind.Colon)) return new DataFormulaReferenceExpression(start);
        string? endSheet = start.SheetName;
        var endToken = Expect(TokenKind.Identifier);
        if (Match(TokenKind.Bang))
        {
            endSheet = endToken.Text;
            endToken = Expect(TokenKind.Identifier);
        }
        if (!DataFormulaReferenceUtility.TryParse(endToken.Text, out var end, endSheet)) throw Error("Expected an A1 range endpoint", endToken.Position);
        return new DataFormulaRangeExpression(new DataFormulaRangeReference(start, end));
    }

    private Token Current => _tokens[Math.Min(_index, _tokens.Count - 1)];
    private Token Consume() => _tokens[_index++];
    private bool Match(TokenKind kind) { if (Current.Kind != kind) return false; _index++; return true; }
    private Token Expect(TokenKind kind) { if (Current.Kind == kind) return Consume(); throw Error($"Expected {kind}", Current.Position); }
    private static DataFormulaParseException Error(string message, int position) => new(message, position);

    private static List<Token> Tokenize(string source)
    {
        var tokens = new List<Token>();
        for (var index = 0; index < source.Length;)
        {
            var c = source[index];
            if (char.IsWhiteSpace(c)) { index++; continue; }
            var start = index;
            if (c == '"')
            {
                index++; var builder = new StringBuilder(); var closed = false;
                while (index < source.Length)
                {
                    if (source[index] != '"') { builder.Append(source[index++]); continue; }
                    if (index + 1 < source.Length && source[index + 1] == '"') { builder.Append('"'); index += 2; continue; }
                    index++; closed = true; break;
                }
                if (!closed) throw Error("Unterminated string literal", start);
                tokens.Add(new(TokenKind.String, builder.ToString(), start)); continue;
            }
            if (c == (char)39)
            {
                index++; var builder = new StringBuilder(); var closed = false;
                while (index < source.Length)
                {
                    if (source[index] != (char)39) { builder.Append(source[index++]); continue; }
                    if (index + 1 < source.Length && source[index + 1] == (char)39) { builder.Append((char)39); index += 2; continue; }
                    index++; closed = true; break;
                }
                if (!closed) throw Error("Unterminated sheet name", start);
                tokens.Add(new(TokenKind.Identifier, builder.ToString(), start)); continue;
            }
            if (char.IsDigit(c) || (c == '.' && index + 1 < source.Length && char.IsDigit(source[index + 1])))
            {
                index++; while (index < source.Length && (char.IsDigit(source[index]) || source[index] == '.')) index++;
                if (index < source.Length && source[index] is 'e' or 'E') { index++; if (index < source.Length && source[index] is '+' or '-') index++; while (index < source.Length && char.IsDigit(source[index])) index++; }
                tokens.Add(new(TokenKind.Number, source[start..index], start)); continue;
            }
            if (char.IsLetter(c) || c is '_' or '$' or '#')
            {
                index++; while (index < source.Length && (char.IsLetterOrDigit(source[index]) || source[index] is '_' or '$' or '.')) index++;
                tokens.Add(new(TokenKind.Identifier, source[start..index], start)); continue;
            }
            switch (c)
            {
                case '+': tokens.Add(new(TokenKind.Plus, "+", start)); index++; break;
                case '-': tokens.Add(new(TokenKind.Minus, "-", start)); index++; break;
                case '*': tokens.Add(new(TokenKind.Star, "*", start)); index++; break;
                case '/': tokens.Add(new(TokenKind.Slash, "/", start)); index++; break;
                case '^': tokens.Add(new(TokenKind.Caret, "^", start)); index++; break;
                case '&': tokens.Add(new(TokenKind.Ampersand, "&", start)); index++; break;
                case '%': tokens.Add(new(TokenKind.Percent, "%", start)); index++; break;
                case '(': tokens.Add(new(TokenKind.LeftParen, "(", start)); index++; break;
                case ')': tokens.Add(new(TokenKind.RightParen, ")", start)); index++; break;
                case ',': case ';': tokens.Add(new(TokenKind.Comma, c.ToString(), start)); index++; break;
                case ':': tokens.Add(new(TokenKind.Colon, ":", start)); index++; break;
                case '!': tokens.Add(new(TokenKind.Bang, "!", start)); index++; break;
                case '=': tokens.Add(new(TokenKind.Equal, "=", start)); index++; break;
                case '<':
                    if (index + 1 < source.Length && source[index + 1] == '=') { tokens.Add(new(TokenKind.LessEqual, "<=", start)); index += 2; }
                    else if (index + 1 < source.Length && source[index + 1] == '>') { tokens.Add(new(TokenKind.NotEqual, "<>", start)); index += 2; }
                    else { tokens.Add(new(TokenKind.Less, "<", start)); index++; }
                    break;
                case '>':
                    if (index + 1 < source.Length && source[index + 1] == '=') { tokens.Add(new(TokenKind.GreaterEqual, ">=", start)); index += 2; }
                    else { tokens.Add(new(TokenKind.Greater, ">", start)); index++; }
                    break;
                default: throw Error($"Unexpected character '{c}'", start);
            }
        }
        tokens.Add(new(TokenKind.End, string.Empty, source.Length));
        return tokens;
    }

    private enum TokenKind { End, Number, String, Identifier, Plus, Minus, Star, Slash, Caret, Ampersand, Percent, Equal, NotEqual, Less, LessEqual, Greater, GreaterEqual, LeftParen, RightParen, Comma, Colon, Bang }
    private sealed record Token(TokenKind Kind, string Text, int Position);
}
