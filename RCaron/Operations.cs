namespace RCaron;

public static class Operations
{
    // other
    public const string AssignmentOp = "=";
    
    // equality
    public const string IsEqualOp = "==";
    public const string IsNotEqualOp = "!=";
    public const string IsGreaterOp = ">";
    public const string IsGreaterOrEqualOp = ">=";
    public const string IsLessOp = "<";
    public const string IsLessOrEqualOp = "<=";
    
    // boolean
    public const string AndOp = "&&";
    public const string OrOp = "||";

    // unary
    public const string UnaryIncrementOp = "++";
    public const string UnaryDecrementOp = "--";

    // math
    public const string SumOp = "+";
    public const string SubtractOp = "-";
    public const string MultiplyOp = "*";
    public const string DivideOp = "/";
    public const string ModuloOp = "%";
    
    // pipeline
    public const string PipelineOp = "|";
}

public enum OperationEnum : byte
{
    Invalid = 0,
    // other
    Assignment,
    // equality
    IsEqual,
    IsNotEqual,
    IsGreater,
    IsGreaterOrEqual,
    IsLess,
    IsLessOrEqual,
    // boolean
    And,
    Or,
    // unary
    UnaryIncrement,
    UnaryDecrement,
    // math
    Sum,
    Subtract,
    Multiply,
    Divide,
    Modulo,
    Pipeline,
}