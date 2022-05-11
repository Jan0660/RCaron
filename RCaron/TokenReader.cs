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
        // var txt = text.AsSpan()[position..];

        void Skip(int count)
        {
            position += count;
        }

        var initialPosition = position;

        // variable
        if (txt[position] == '$')
        {
            Skip(1);
            var index = CollectAlphaNumeric(txt[position..]);
            if (index == 0)
                return null;
            position += index;
            return new PosToken(TokenType.VariableIdentifier, (initialPosition, position));
        }
        // string
        else if (txt[position] == '\'')
        {
            Skip(1);
            var index = CollectString(txt[position..]);
            if (index == 0)
                return null;
            position += index;
            return new PosToken(TokenType.String, (initialPosition, position));
        }
        // whitespace
        else if (char.IsWhiteSpace(txt[position]))
        {
            position++;
            while (position < txt.Length && char.IsWhiteSpace(txt[position]))
                ++position;
            return new PosToken(TokenType.Whitespace, (initialPosition, position));
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
            return new PosToken(isDecimal ? TokenType.DecimalNumber : TokenType.Number, (initialPosition, position));
        }
        // operation
        else
        {
            var index = CollectOperation(txt[position..]);
            if (index == 0)
            {
                position = txt[position..].IndexOf(' ') + position;
                return new PosToken(TokenType.Keyword, (initialPosition, position));
            }

            position += index;
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

    public int CollectOperation(in ReadOnlySpan<char> span)
    {
        if (IsMatch(in span, Operations.IsEqualOp))
            return 2;
        if (IsMatch(in span, Operations.IsNotEqualOp))
            return 2;
        if (IsMatch(in span, Operations.AssignmentOp))
            return 1;

        return 0;
    }
}