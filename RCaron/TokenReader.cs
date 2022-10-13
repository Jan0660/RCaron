using System.Diagnostics;
using Log73;

namespace RCaron;

// todo(perf): could be a struct so that it is kept on the stack?
public class TokenReader
{
    int position;
    string text;
    public bool ReturnIgnored { get; set; }
    public static readonly PosToken IgnorePosToken = new(TokenType.Ignore, (0, 0));

    public TokenReader(string text, bool returnIgnored = false)
    {
        this.text = text;
        this.position = 0;
        ReturnIgnored = returnIgnored;
    }

    public PosToken? Read()
    {
        if (position >= text.Length)
        {
            return null;
        }

        var txt = text.AsSpan();

        void Skip(int count)
        {
            position += count;
        }

        var initialPosition = position;

        // variable
        if (txt[position] == '$')
        {
            Skip(1);
            var index = CollectAlphaNumericAndSome(txt[position..]);
            if (index == 0)
                return null;
            position += index;
            return new ValuePosToken(TokenType.VariableIdentifier, (initialPosition, position));
        }
        // shebang
        else if (txt[position] == '#' && txt[position + 1] == '!')
        {
            position += 2;
            // collect until line ending
            while (position != txt.Length && txt[position] != '\n')
                position++;
            if (!ReturnIgnored)
                return IgnorePosToken;
            return new PosToken(TokenType.Ignore, (initialPosition, position));
        }
        // string
        else if (txt[position] == '\'')
        {
            Skip(1);
            var (index, str) = CollectString(txt[position..]);
            if (index == 0)
                return null;
            position += index;
            return new StringValuePosToken(TokenType.String, (initialPosition, position), str);
        }
        // whitespace
        else if (char.IsWhiteSpace(txt[position]))
        {
            position++;
            while (position < txt.Length && char.IsWhiteSpace(txt[position]))
                ++position;
            if (!ReturnIgnored)
                return IgnorePosToken;
            return new PosToken(TokenType.Whitespace, (initialPosition, position));
        }
        // comma
        else if (txt[position] == ',')
        {
            position++;
            return new PosToken(TokenType.Comma, (initialPosition, position));
        }
        // line ending - semicolon
        else if (txt[position] == ';')
        {
            position++;
            return new PosToken(TokenType.LineEnding, (initialPosition, position));
        }
        else if (txt[position] == '{')
        {
            position++;
            return new BlockPosToken(TokenType.BlockStart, (initialPosition, position));
        }
        else if (txt[position] == '}')
        {
            position++;
            return new BlockPosToken(TokenType.BlockEnd, (initialPosition, position));
        }
        else if (txt[position] == '(')
        {
            position++;
            return new BlockPosToken(TokenType.SimpleBlockStart, (initialPosition, position));
        }
        else if (txt[position] == ')')
        {
            position++;
            return new BlockPosToken(TokenType.SimpleBlockEnd, (initialPosition, position));
        }
        // (decimal|integer) number
        else if (char.IsDigit(txt[position]))
        {
            var (index, isDecimal) = CollectAnyNumber(txt[position..]);
            position += index;
            return new ValuePosToken(isDecimal ? TokenType.DecimalNumber : TokenType.Number,
                (initialPosition, position));
        }
        // single line comment
        else if (txt[position] == '/' && txt[position + 1] == '/')
        {
            position += 2;
            while (position < txt.Length && txt[position] != '\n')
                position++;
            if (!ReturnIgnored)
                return IgnorePosToken;
            return new PosToken(TokenType.Comment, (initialPosition, position));
        }
        // multiline line comment
        else if (txt[position] == '/' && txt[position + 1] == '*')
        {
            position += 2;
            while (txt[position] != '*' && txt[position + 1] != '/')
                position++;
            position += 3;
            if (!ReturnIgnored)
                return IgnorePosToken;
            return new PosToken(TokenType.Comment, (initialPosition, position));
        }
        // extern thing
        else if (txt[position] == '#')
        {
            position++;
            position += CollectAlphaNumericAndSomeAndDot(txt[position..]);
            return new PosToken(TokenType.ExternThing, (initialPosition, position));
        }
        // array literal start
        else if (txt[position] == '@' && txt[position + 1] == '(')
        {
            position++;
            return new PosToken(TokenType.ArrayLiteralStart, (initialPosition, position));
        }
        // range operator
        else if (txt[position] == '.' && txt[position + 1] == '.' && txt[position + 2] != '.')
        {
            position += 2;
            return new PosToken(TokenType.Operator, (initialPosition, position));
        }
        // dot
        else if (txt[position] == '.')
        {
            position++;
            return new PosToken(TokenType.Dot, (initialPosition, position));
        }
        // normal array accesser start
        else if (txt[position] == '[')
        {
            position++;
            return new BlockPosToken(TokenType.IndexerStart, (initialPosition, position));
        }
        // normal array accesser end
        else if (txt[position] == ']')
        {
            position++;
            return new BlockPosToken(TokenType.IndexerEnd, (initialPosition, position));
        }
        // colon
        else if (txt[position] == ':')
        {
            position++;
            return new PosToken(TokenType.Colon, (initialPosition, position));
        }
        // operation
        else
        {
            var (index, tokenType) = CollectOperation(txt[position..]);
            // collect a keyword e.g. "println"
            if (index == 0)
            {
                index = CollectAlphaNumericAndSomeAndDash(txt[position..]);
                position += index;
                return new PosToken(TokenType.Keyword, (initialPosition, position));
            }

            position += index;
            // match "value" operations
            if (tokenType == TokenType.Operator || tokenType == TokenType.MathOperator)
                return new ValuePosToken(tokenType, (initialPosition, position));

            return new PosToken(tokenType, (initialPosition, position));
        }
    }

    public (int index, bool isDecimal) CollectAnyNumber(in ReadOnlySpan<char> span)
    {
        var index = 0;
        var isDecimal = false;
        while (char.IsDigit(span[index]) || span[index] == '.')
        {
            if (span[index] == '.' && char.IsDigit(span[index + 1]))
                isDecimal = true;
            else if (span[index] == '.')
                return (index, isDecimal);
            index++;
        }

        return (index, isDecimal);
    }

    public int CollectAlphaNumeric(in ReadOnlySpan<char> span)
    {
        var index = 0;
        while ((span[index] >= '0' && span[index] <= '9') || (span[index] >= 'a' && span[index] <= 'z') ||
               (span[index] >= 'A' && span[index] <= 'Z'))
            index++;
        return index;
    }

    public int CollectAlphaNumericAndSome(in ReadOnlySpan<char> span)
    {
        var index = 0;
        while ((span[index] >= '0' && span[index] <= '9') || (span[index] >= 'a' && span[index] <= 'z') ||
               (span[index] >= 'A' && span[index] <= 'Z') || span[index] == '_')
            index++;
        return index;
    }

    public int CollectAlphaNumericAndSomeAndDash(in ReadOnlySpan<char> span)
    {
        var index = 0;
        while ((span[index] >= '0' && span[index] <= '9') || (span[index] >= 'a' && span[index] <= 'z') ||
               (span[index] >= 'A' && span[index] <= 'Z') || span[index] == '_' || span[index] == '-')
            index++;
        return index;
    }

    public int CollectAlphaNumericAndSomeAndDot(in ReadOnlySpan<char> span)
    {
        var index = 0;
        while ((span[index] >= '0' && span[index] <= '9') || (span[index] >= 'a' && span[index] <= 'z') ||
               (span[index] >= 'A' && span[index] <= 'Z') || (span[index] == '_' || span[index] == '.'))
            index++;
        return index;
    }

    public (int endIndex, string value) CollectString(in ReadOnlySpan<char> span)
    {
        // get index of unescaped ending '
        var index = 0;
        while (span[index] != '\'')
        {
            if (span[index] == '\\')
                index++;
            index++;
        }

        index++;
        // assemble the string
        Span<char> g = stackalloc char[span.Length];
        var str = new SpanStringBuilder(ref g);
        for (var i = 0; i < index-1; i++)
        {
            if (span[i] == '\\')
            {
                i++;
                // single quote
                if(span[i] == '\'')
                    str.Append('\'');
                // backslash
                else if(span[i] == '\\')
                    str.Append('\\');
                // null
                else if(span[i] == '0')
                    str.Append('\0');
                // alert
                else if(span[i] == 'a')
                    str.Append('\a');
                // backspace
                else if(span[i] == 'b')
                    str.Append('\b');
                // form feed
                else if(span[i] == 'f')
                    str.Append('\f');
                // new line
                else if(span[i] == 'n')
                    str.Append('\n');
                // carriage return
                else if(span[i] == 'r')
                    str.Append('\r');
                // horizontal tab
                else if(span[i] == 't')
                    str.Append('\t');
                // vertical tab
                else if(span[i] == 'v')
                    str.Append('\v');
                else
                    throw new RCaronException($"invalid character to escape: {span[i]}", RCaronExceptionCode.InvalidEscape);
                continue;
            }

            str.Append(span[i]);
        }

        return (index, str.ToString());
    }

    public bool IsMatch(in ReadOnlySpan<char> span, ReadOnlySpan<char> toMatch)
    {
        for (var i = 0; i < toMatch.Length; i++)
        {
            if (span[i] != toMatch[i])
                return false;
        }

        return true;
    }

    public (int, TokenType tokenType) CollectOperation(in ReadOnlySpan<char> span)
    {
        // comparison
        if (IsMatch(in span, Operations.IsEqualOp))
            return (2, TokenType.ComparisonOperation);
        if (IsMatch(in span, Operations.IsNotEqualOp))
            return (2, TokenType.ComparisonOperation);
        if (IsMatch(in span, Operations.IsGreaterOrEqualOp))
            return (2, TokenType.ComparisonOperation);
        if (IsMatch(in span, Operations.IsLessOrEqualOp))
            return (2, TokenType.ComparisonOperation);
        if (IsMatch(in span, Operations.IsGreaterOp))
            return (1, TokenType.ComparisonOperation);
        if (IsMatch(in span, Operations.IsLessOp))
            return (1, TokenType.ComparisonOperation);
        // logical
        if (IsMatch(in span, Operations.AndOp))
            return (2, TokenType.LogicalOperation);
        if (IsMatch(in span, Operations.OrOp))
            return (2, TokenType.LogicalOperation);
        // other
        if (IsMatch(in span, Operations.AssignmentOp))
            return (1, TokenType.Operation);
        // unary
        if (IsMatch(in span, Operations.UnaryIncrementOp))
            return (2, TokenType.UnaryOperation);
        if (IsMatch(in span, Operations.UnaryDecrementOp))
            return (2, TokenType.UnaryOperation);
        // math
        if (IsMatch(in span, Operations.SumOp))
            return (1, TokenType.MathOperator);
        if (IsMatch(in span, Operations.SubtractOp))
            return (1, TokenType.MathOperator);
        if (IsMatch(in span, Operations.MultiplyOp))
            return (1, TokenType.MathOperator);
        if (IsMatch(in span, Operations.DivideOp))
            return (1, TokenType.MathOperator);
        if (IsMatch(in span, Operations.ModuloOp))
            return (1, TokenType.MathOperator);
        return (0, 0);
    }
}