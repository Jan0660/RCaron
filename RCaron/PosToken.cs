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
    Dot,
    DotGroup,
    CodeBlock,
    ArrayAccessorStart,
    ArrayAccessorEnd,
    ArrayAccessor,
    Colon,
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

    public bool IsDotJoinableSomething()
        => Type switch
        {
            TokenType.Dot => true,
            TokenType.Number => true,
            TokenType.VariableIdentifier => true,
            TokenType.Keyword => true,
            TokenType.KeywordCall => true,
            TokenType.ArrayAccessor => true,
            TokenType.Colon => true,
            TokenType.ExternThing => true,
            _ => false,
        };
}

public class BlockPosToken : PosToken
{
    public int Depth { get; set; } = -1;
    public int Number { get; set; } = -1;

    public BlockPosToken(TokenType type, (int Start, int End) position) : base(type, position)
    {
    }
}

public class CodeBlockToken : ValuePosToken
{
    public List<Line> Lines;

    public CodeBlockToken(List<Line> lines) : base(TokenType.CodeBlock,
        (((TokenLine)lines[0]).Tokens[0].Position.Start, ((TokenLine)lines[^1]).Tokens[^1].Position.End))
    {
        Lines = lines;
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
    /// <summary>
    /// lower-cased
    /// </summary>
    public string Name { get; }

    public CallLikePosToken(TokenType type, (int Start, int End) position, PosToken[][] arguments, int nameEndIndex, string name
        ) :
        base(type, position)
    {
        Arguments = arguments;
        NameEndIndex = nameEndIndex;
        Name = name;
    }

    public string GetName(string text)
        => text[Position.Start..NameEndIndex];

    public bool NameEquals(in string text, in string b)
        => text.AsSpan()[Position.Start..NameEndIndex].SequenceEqual(b);

    public bool ArgumentsEmpty()
        => Arguments.Length == 0 || (Arguments.Length == 1 && Arguments[0].Length == 0);
}

public class DotGroupPosToken : PosToken
{
    public PosToken[] Tokens { get; }

    public DotGroupPosToken(TokenType type, (int Start, int End) position, PosToken[] tokens) : base(type,
        position)
    {
        Tokens = tokens;
    }
}

public class ArrayAccessorToken : PosToken
{
    public PosToken[] Tokens { get; }
    public ArrayAccessorToken(PosToken[] tokens) : base(TokenType.ArrayAccessor, (tokens[0].Position.Start, tokens[^1].Position.End))
    {
        Tokens = tokens;
    }
}