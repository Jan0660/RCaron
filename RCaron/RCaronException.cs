using ExceptionCode = RCaron.RCaronExceptionCode;

namespace RCaron;

public enum RCaronExceptionCode
{
    FunctionNotFound,
    NamedFunctionArgumentNotFound,
    FunctionArgumentsLeftUnassigned,
    VariableNotFound,
    MethodNotFound,
    ParseInvalidLine,
    LeftOverFunctionPositionalArgument,
    MethodNoSuitableMatch,
    UnknownOperator,
    ExpectedNotNull,
    TypeMismatch,
    ExternalTypeNotFound,
    NullInDotThing,
    CannotResolveInDotThing
}

public class RCaronException : Exception
{
    public ExceptionCode Code { get; }

    public RCaronException(string message, ExceptionCode exceptionCode) : base(message)
    {
        Code = exceptionCode;
    }
    
    public static RCaronException VariableNotFound(string name)
        => new($"variable '{name}' does not exist in this scope", ExceptionCode.VariableNotFound);
    
    public static RCaronException NullInTokens(in Span<PosToken> tokens, string raw, int index)
        => new(
                $"null resolved in '{tokens[index].ToString(raw)}'(index={index}) in '{raw[tokens[0].Position.Start..tokens[^1].Position.End]}'",
                ExceptionCode.NullInDotThing);
}