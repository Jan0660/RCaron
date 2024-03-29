using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace RCaron;

public enum TokenType : byte
{
    Number,
    VariableIdentifier,
    ComparisonOperation,
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
    KeywordCall,
    Comma,
    UnaryOperation,
    Comment,
    ExternThing,
    ArrayLiteralStart,
    Dot,
    DotGroup,
    CodeBlock,
    IndexerStart,
    IndexerEnd,
    Indexer,
    Colon,
    Ignore,
    MathOperator,
    Operation,
    EqualityOperationGroup,
    LogicalOperation,
    LogicalOperationGroup,
    Path,
    Range,
    EndOfFile,
    Group,
    TokenGroup,
    HexNumber,
    PipelineOperator,
    Pipeline,
    CharLiteral,
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

    public bool EqualsStringCaseInsensitive(in string text, in string b)
        => text.AsSpan()[Position.Start..Position.End].Equals(b, StringComparison.InvariantCultureIgnoreCase);

    // the span doesnt seem to work for some reason?
    public ReadOnlySpan<char> ToSpan(in ReadOnlySpan<char> span)
        => span[Position.Start..(Position.End)];

    public bool IsDotJoinable()
        => Type switch
        {
            TokenType.Dot => true,
            TokenType.Number => true,
            TokenType.VariableIdentifier => true,
            TokenType.String => true,
            TokenType.Keyword => true,
            TokenType.KeywordCall => true,
            TokenType.Indexer => true,
            TokenType.IndexerStart => true,
            TokenType.IndexerEnd => true,
            TokenType.Colon => true,
            TokenType.ExternThing => true,
            TokenType.Path => true,
            TokenType.Range => true,
            _ => false,
        };

    public bool IsLiteral()
        => Type switch
        {
            TokenType.Number => true,
            TokenType.DecimalNumber => true,
            TokenType.String => true,
            _ => false,
        };

    public bool IsKeywordWithIgnoredWhitespace(in string raw)
        => Type == TokenType.Keyword && this.ToSpan(raw) switch
        {
            "if" => true,
            "else" => true,
            "for" => true,
            "qfor" => true,
            "foreach" => true,
            "while" => true,
            "dowhile" => true,
            "switch" => true,
            _ => false,
        };
}

public class ConstToken : ValuePosToken
{
    public object Value { get; }
    public bool IsExecutable { get; }

    public ConstToken(TokenType type, (int Start, int End) position, object value, bool isExecutable = false) : base(
        type, position)
    {
        Value = value;
        IsExecutable = isExecutable;
    }
}

public class VariableToken : ValuePosToken
{
    public string Name { get; }

    public VariableToken((int Start, int End) position, string name) : base(TokenType.VariableIdentifier, position)
    {
        Name = name;
    }
}

public class KeywordToken : PosToken
{
    public string String { get; }
    public bool IsExecutable { get; set; }

    public KeywordToken((int Start, int End) position, string str, bool isExecutable = false) : base(TokenType.Keyword,
        position)
    {
        String = str;
        IsExecutable = isExecutable;
    }
}

public class ExternThingToken : PosToken
{
    public string String { get; }

    public ExternThingToken((int Start, int End) position, string str) : base(TokenType.ExternThing, position)
    {
        String = str;
    }
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

public class GroupValuePosToken : ValuePosToken
{
    public PosToken[] Tokens { get; }

    public GroupValuePosToken(TokenType type, (int Start, int End) position, PosToken[] tokens) : base(type,
        position)
    {
        Tokens = tokens;
    }
}

public class TokenGroupPosToken : ValuePosToken
{
    public PosToken[] Tokens { get; }

    public TokenGroupPosToken(TokenType type, (int Start, int End) position, PosToken[] tokens) : base(type,
        position)
    {
        Tokens = tokens;
    }
}

public class ComparisonValuePosToken : ValuePosToken
{
    public ValuePosToken Left { get; }
    public ValuePosToken Right { get; }
    public OperationPosToken ComparisonToken { get; set; }
    public CallSite<Func<CallSite, object?, object?, object>>? CallSite { get; set; }

    public ComparisonValuePosToken(ValuePosToken left, ValuePosToken right, OperationPosToken comparisonToken,
        (int Start, int End) position) : base(
        TokenType.EqualityOperationGroup, position)
    {
        Left = left;
        Right = right;
        ComparisonToken = comparisonToken;
    }
}

public class LogicalOperationValuePosToken : ValuePosToken
{
    public ValuePosToken Left { get; }
    public ValuePosToken Right { get; }
    public OperationPosToken ComparisonToken { get; set; }

    public LogicalOperationValuePosToken(ValuePosToken left, ValuePosToken right, OperationPosToken comparisonToken,
        (int Start, int End) position) : base(
        TokenType.LogicalOperationGroup, position)
    {
        Left = left;
        Right = right;
        ComparisonToken = comparisonToken;
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

    public CallLikePosToken(TokenType type, (int Start, int End) position, PosToken[][] arguments, int nameEndIndex,
        string name
    ) :
        base(type, position)
    {
        Arguments = arguments;
        NameEndIndex = nameEndIndex;
        Name = name;
    }

    public bool NameEquals(in string text, in string b)
        => text.AsSpan()[Position.Start..NameEndIndex].SequenceEqual(b);

    public bool ArgumentsEmpty()
        => Arguments.Length == 0 || (Arguments.Length == 1 && Arguments[0].Length == 0);
}

public class DotGroupPosToken : ValuePosToken
{
    public PosToken[] Tokens { get; }

    public DotGroupPosToken(TokenType type, (int Start, int End) position, PosToken[] tokens) : base(type,
        position)
    {
        Tokens = tokens;
    }
}

public class IndexerToken : PosToken
{
    public PosToken[] Tokens { get; }
    public CallSite<Func<CallSite, object?, object?, object?>>? CallSite { get; set; }

    public IndexerToken(PosToken[] tokens, (int Start, int End) position) : base(TokenType.Indexer, position)
    {
        Tokens = tokens;
    }
}

public class ValueOperationValuePosToken : ValuePosToken
{
    public OperationEnum Operation { get; }
    public CallSite<Func<CallSite, object, object, object>>? CallSite { get; set; }

    public ValueOperationValuePosToken(TokenType type, (int Start, int End) position, OperationEnum operation) : base(
        type, position)
    {
        Operation = operation;
    }
}

public class OperationPosToken : PosToken
{
    public OperationEnum Operation { get; }

    public OperationPosToken(TokenType type, (int Start, int End) position, OperationEnum operation) : base(type,
        position)
    {
        Operation = operation;
    }
}

public class PipelineValuePosToken : ValuePosToken
{
    public PosToken[] Left { get; }
    public PosToken[] Right { get; }

    public PipelineValuePosToken(PosToken[] left, PosToken[] right) : base(
        TokenType.Pipeline, (left[0].Position.Start, right[^1].Position.End))
    {
        Left = left;
        Right = right;
    }
}
