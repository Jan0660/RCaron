using System.Diagnostics;

namespace RCaron;

public enum TokenType : byte
{
    Number,
    VariableIdentifier,
    Operation,
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

    // the span doesnt seem to work for some reason?
    // public ReadOnlySpan<char> ToSpan(in ReadOnlySpan<char> span)
    //     => span[Position.Start..(Position.End)];
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
    // public int Depth { get; set; } = -1;
    // // public int ParentNumber { get; set; } = -1;
    // public int Number { get; set; } = -1;

    public ValuePosToken(TokenType type, (int Start, int End) position) : base(type, position)
    {
    }
}

public class ValueGroupPosToken : ValuePosToken
{
    public ValuePosToken[] ValueTokens { get; }
    public ValueGroupPosToken(TokenType type, (int Start, int End) position, ValuePosToken[] tokens) : base(type, position)
    {
        ValueTokens = tokens;
    }
}