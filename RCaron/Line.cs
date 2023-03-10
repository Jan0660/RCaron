using System.Diagnostics;

namespace RCaron;

[DebuggerDisplay("Type = {Type}")]
public class Line
{
    public LineType Type { get; set; }
    public Line(LineType type)
    {
        Type = type;
    }
}
public class TokenLine : Line
{
    public PosToken[] Tokens { get; set; }

    public TokenLine(PosToken[] tokens, LineType type) : base(type)
    {
        Tokens = tokens;
    }
}
public class SingleTokenLine : Line
{
    public PosToken Token { get; set; }

    public SingleTokenLine(PosToken token, LineType type) : base(type)
    {
        Token = token;
    }
}

public class ForLoopLine : Line
{
    public CallLikePosToken CallToken { get; }
    public Line Initializer { get; }
    public Line Iterator { get; }
    public CodeBlockToken Body { get; }
    public ForLoopLine(CallLikePosToken callToken, Line initializer, Line iterator, CodeBlockToken body, LineType lineType = LineType.ForLoop) : base(lineType)
    {
        CallToken = callToken;
        Initializer = initializer;
        Iterator = iterator;
        Body = body;
    }
}

public class CodeBlockLine : Line
{
    public CodeBlockToken Token { get; }
    public CodeBlockLine(CodeBlockToken token) : base(LineType.CodeBlock)
    {
        Token = token;
    }
}

public enum LineType : byte
{
    VariableAssignment,
    IfStatement,
    BlockStuff,
    KeywordPlainCall,
    LoopLoop,
    WhileLoop,
    DoWhileLoop,
    Function,
    KeywordCall,
    ForLoop,
    UnaryOperation,
    CodeBlock,
    AssignerAssignment,
    ForeachLoop,
    QuickForLoop,
    DotGroupCall,
    PropertyWithoutInitializer,
    SwitchStatement,
    SwitchCase,
    ElseIfStatement,
    ElseStatement,
    TryBlock,
    CatchBlock,
    FinallyBlock,
    ClassDefinition,
    LetVariableAssignment,
    InvalidLine
}