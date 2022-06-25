namespace RCaron;

// todo(perf): could be a struct so that it is kept on the stack?
public class TokenReader
{
    int position;
    string text;

    public TokenReader(string text)
    {
        this.text = text;
        this.position = 0;
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
        // string
        else if (txt[position] == '\'')
        {
            Skip(1);
            var index = CollectString(txt[position..]);
            if (index == 0)
                return null;
            position += index;
            return new ValuePosToken(TokenType.String, (initialPosition, position));
        }
        // whitespace
        else if (char.IsWhiteSpace(txt[position]))
        {
            position++;
            while (position < txt.Length && char.IsWhiteSpace(txt[position]))
                ++position;
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
        else if (txt[position] == '/' && txt[position+1] == '/')
        {
            position += 2;
            while (position < txt.Length && txt[position] != '\n')
                position++;
            return new PosToken(TokenType.Comment, (initialPosition, position));
        }
        // multiline line comment
        else if (txt[position] == '/' && txt[position+1] == '*')
        {
            position += 2;
            while (txt[position] != '*' && txt[position + 1] != '/')
                position++;
            position+=3;
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
            return new BlockPosToken(TokenType.ArrayAccessorStart, (initialPosition, position));
        }
        // normal array accesser end
        else if (txt[position] == ']')
        {
            position++;
            return new BlockPosToken(TokenType.ArrayAccessorEnd, (initialPosition, position));
        }
        // operation
        else
        {
            var (index, tokenType) = CollectOperation(txt[position..]);
            // collect a keyword e.g. "println"
            if (index == 0)
            {
                index = CollectAlphaNumericAndSome(txt[position..]);
                position += index;
                return new PosToken(TokenType.Keyword, (initialPosition, position));
            }

            position += index;
            // match "value" operations
            if (tokenType == TokenType.Operator)
                return new ValuePosToken(TokenType.Operator, (initialPosition, position));

            return new PosToken(tokenType, (initialPosition, position));
        }
    }

    public (int index, bool isDecimal) CollectAnyNumber(in ReadOnlySpan<char> span)
    {
        var index = 0;
        var isDecimal = false;
        while (char.IsDigit(span[index]) || span[index] == '.')
        {
            if (span[index] == '.' && char.IsDigit(span[index+1]))
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
               (span[index] >= 'A' && span[index] <= 'Z') || (span[index] == '_'))
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

    public int CollectString(in ReadOnlySpan<char> span)
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
        return index;
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
        if (IsMatch(in span, Operations.UnaryIncrementOp))
            return (2, TokenType.UnaryOperation);
        if (IsMatch(in span, Operations.UnaryDecrementOp))
            return (2, TokenType.UnaryOperation);

        if (IsMatch(in span, Operations.IsEqualOp))
            return (2, TokenType.Operation);
        if (IsMatch(in span, Operations.IsNotEqualOp))
            return (2, TokenType.Operation);
        if (IsMatch(in span, Operations.IsGreaterOrEqualOp))
            return (2, TokenType.Operation);
        if (IsMatch(in span, Operations.IsLessOrEqualOp))
            return (2, TokenType.Operation);
        if (IsMatch(in span, Operations.IsGreaterOp))
            return (1, TokenType.Operation);
        if (IsMatch(in span, Operations.IsLessOp))
            return (1, TokenType.Operation);
        if (IsMatch(in span, Operations.AssignmentOp))
            return (1, TokenType.Operation);

        if (IsMatch(in span, Operations.SumOp))
            return (1, TokenType.Operator);
        if (IsMatch(in span, Operations.SubtractOp))
            return (1, TokenType.Operator);
        if (IsMatch(in span, Operations.MultiplyOp))
            return (1, TokenType.Operator);
        if (IsMatch(in span, Operations.DivideOp))
            return (1, TokenType.Operator);
        if (IsMatch(in span, Operations.ModuloOp))
            return (1, TokenType.Operator);

        return (0, 0);
    }
}