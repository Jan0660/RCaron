using System.Diagnostics;
using System.Runtime.CompilerServices;
using RCaron.Parsing;

namespace RCaron;

[DebuggerDisplay("Type = {Type}")]
public abstract class Line
{
    public LineType Type { get; set; }

    public Line(LineType type)
    {
        Type = type;
    }

    public abstract TextSpan GetLocation();
}

public class TokenLine : Line
{
    public PosToken[] Tokens { get; set; }

    public TokenLine(PosToken[] tokens, LineType type) : base(type)
    {
        Tokens = tokens;
    }

    public override TextSpan GetLocation()
        => TextSpan.FromStartAndEnd(Tokens[0].Position.Start, Tokens[^1].Position.End);
}

public class SingleTokenLine : Line
{
    public PosToken Token { get; set; }

    public SingleTokenLine(PosToken token, LineType type) : base(type)
    {
        Token = token;
    }

    public override TextSpan GetLocation()
        => TextSpan.FromToken(Token);
}

public class ForLoopLine : Line
{
    public CallLikePosToken CallToken { get; }
    public Line? Initializer { get; }
    public Line? Iterator { get; }
    public CodeBlockToken Body { get; }

    public ForLoopLine(CallLikePosToken callToken, Line? initializer, Line? iterator, CodeBlockToken body,
        LineType lineType = LineType.ForLoop) : base(lineType)
    {
        CallToken = callToken;
        Initializer = initializer;
        Iterator = iterator;
        Body = body;
    }

    public override TextSpan GetLocation()
        => TextSpan.FromStartAndEnd(CallToken.Position.Start, Body.Position.End);
}

public class CodeBlockLine : Line
{
    public CodeBlockToken Token { get; }

    public CodeBlockLine(CodeBlockToken token) : base(LineType.CodeBlock)
    {
        Token = token;
    }

    public override TextSpan GetLocation()
        => TextSpan.FromToken(Token);
}

public class UnaryOperationLine : TokenLine
{
    public CallSite<Func<CallSite, object, object, object>>? CallSite { get; set; }

    public UnaryOperationLine(PosToken[] tokens, LineType type) : base(tokens, type)
    {
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
    StaticFunction,
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
    InvalidLine,
    PathCall,
    StaticProperty,
    PipelineRun,
}