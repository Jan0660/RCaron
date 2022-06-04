using System.Diagnostics;

namespace RCaron;

public enum TokenType : byte
{
    Number,
    VariableIdentifier,
    Operation,
    Operator,
    String,
    Whitespace,
    LineEnding,
    Keyword,
    BlockStart,
    BlockEnd,
    SimpleBlockStart,
    SimpleBlockEnd,
    DecimalNumber,
    DumbShit,
    KeywordCall,
    Comma,
    UnaryOperation,
    Comment,
    ExternThing,
    ArrayLiteralStart,
}

[DebuggerDisplay("Type = {Type}")]
public class PosToken
{
    public (int Start, int End) Position { get; }
    public TokenType Type { get; }

    public PosToken(TokenType type, (int Start, int End) position)
    {
        Type = type;
        Position = position;
    }

    public string ToString(string text)
        => text[Position.Start..(Position.End)];

    public bool EqualsString(in string text, in string b)
        => text.AsSpan()[Position.Start..Position.End].SequenceEqual(b);

    // the span doesnt seem to work for some reason?
    public ReadOnlySpan<char> ToSpan(in ReadOnlySpan<char> span)
        => span[Position.Start..(Position.End)];
}

public class BlockPosToken : PosToken
{
    public int Depth { get; set; } = -1;
    public int Number { get; set; } = -1;

    public BlockPosToken(TokenType type, (int Start, int End) position) : base(type, position)
    {
    }
}

public class ValuePosToken : PosToken
{
    public ValuePosToken(TokenType type, (int Start, int End) position) : base(type, position)
    {
    }
}

public class ValueGroupPosToken : ValuePosToken
{
    public ValuePosToken[] ValueTokens { get; }

    public ValueGroupPosToken(TokenType type, (int Start, int End) position, ValuePosToken[] tokens) : base(type,
        position)
    {
        ValueTokens = tokens;
    }
}

public class CallLikePosToken : ValuePosToken
{
    public PosToken[][] Arguments { get; set; }
    public int NameEndIndex { get; }
    public TokenType OriginalThingTokenType { get; }

    public CallLikePosToken(TokenType type, (int Start, int End) position, PosToken[][] arguments, int nameEndIndex, TokenType originalThingTokenType) :
        base(type, position)
    {
        Arguments = arguments;
        NameEndIndex = nameEndIndex;
        OriginalThingTokenType = originalThingTokenType;
    }

    public string GetName(string text)
        => text[Position.Start..NameEndIndex];

    public bool NameEquals(in string text, in string b)
        => text.AsSpan()[Position.Start..NameEndIndex].SequenceEqual(b);

    public bool ArgumentsEmpty()
        => Arguments.Length == 0 || (Arguments.Length == 1 && Arguments[0].Length == 0);
}