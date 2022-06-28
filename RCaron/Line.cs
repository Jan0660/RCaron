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
}