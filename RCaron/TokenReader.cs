namespace RCaron;

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
        // operation
        else
        {
            var (index, isValueOperator) = CollectOperation(txt[position..]);
            // collect a keyword e.g. "println"
            if (index == 0)
            {
                index = CollectAlphaNumericAndSome(txt[position..]);
                position += index;
                return new PosToken(TokenType.Keyword, (initialPosition, position));
            }

            position += index;
            // match "value" operations
            var op = txt[initialPosition..position];
            if (isValueOperator
                // op.SequenceEqual(Operations.SumOp) || op.SequenceEqual(Operations.SubtractOp) ||
                // op.SequenceEqual(Operations.MultiplyOp) || op.SequenceEqual(Operations.DivideOp) ||
                // op.SequenceEqual(Operations.ModuloOp)
                )
                return new ValuePosToken(TokenType.Operator, (initialPosition, position));

            return new PosToken(TokenType.Operation, (initialPosition, position));
        }

        return null;
    }

    public (int index, bool isDecimal) CollectAnyNumber(in ReadOnlySpan<char> span)
    {
        var index = 0;
        var isDecimal = false;
        // todo: isDecimal turn it on smartly ok?
        while (char.IsDigit(span[index]) || span[index] == '.')
        {
            if (span[index] == '.')
                isDecimal = true;
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

    public (int, bool isValueOperator) CollectOperation(in ReadOnlySpan<char> span)
    {
        if (IsMatch(in span, Operations.IsEqualOp))
            return (2, false);
        if (IsMatch(in span, Operations.IsNotEqualOp))
            return (2, false);
        if (IsMatch(in span, Operations.IsGreaterOrEqualOp))
            return (2, false);
        if (IsMatch(in span, Operations.IsLessOrEqualOp))
            return (2, false);
        if (IsMatch(in span, Operations.IsGreaterOp))
            return (1, false);
        if (IsMatch(in span, Operations.IsLessOp))
            return (1, false);
        if (IsMatch(in span, Operations.AssignmentOp))
            return (1, false);
        if (IsMatch(in span, Operations.SumOp))
            return (1, true);
        if (IsMatch(in span, Operations.SubtractOp))
            return (1, true);
        if (IsMatch(in span, Operations.MultiplyOp))
            return (1, true);
        if (IsMatch(in span, Operations.DivideOp))
            return (1, true);
        if (IsMatch(in span, Operations.ModuloOp))
            return (1, true);

        return (0, false);
    }
}