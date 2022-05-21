namespace RCaron;

public struct Line
{
    public PosToken[] Tokens { get; set; }
    public LineType Type { get; set; }

    public Line(PosToken[] tokens, LineType type)
    {
        Tokens = tokens;
        Type = type;
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
}