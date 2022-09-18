using ExceptionCode = RCaron.RCaronExceptionCode;

namespace RCaron;

public enum RCaronExceptionCode
{
    NamedArgumentNotFound,
    ArgumentsLeftUnassigned,
    VariableNotFound,
    MethodNotFound,
    ParseInvalidLine,
    LeftOverPositionalArgument,
    MethodNoSuitableMatch,
    UnknownOperator,
    ExpectedNotNull,
    TypeMismatch,
    ExternalTypeNotFound,
    NullInDotThing,
    CannotResolveInDotThing,
    TypeNotFound,
}

public class RCaronException : Exception
{
    public ExceptionCode Code { get; }

    public RCaronException(string message, ExceptionCode exceptionCode) : base(message)
    {
        Code = exceptionCode;
    }
    
    public static RCaronException VariableNotFound(ReadOnlySpan<char> name)
        => new($"variable '{name}' does not exist in this scope", ExceptionCode.VariableNotFound);
    
    public static RCaronException NullInTokens(in Span<PosToken> tokens, string raw, int index)
        => new(
                $"null resolved in '{tokens[index].ToString(raw)}'(index={index}) in '{raw[tokens[0].Position.Start..tokens[^1].Position.End]}'",
                ExceptionCode.NullInDotThing);

    public static RCaronException LeftOverPositionalArgument()
        => new("positional argument encountered with no unassigned arguments left",
            ExceptionCode.LeftOverPositionalArgument);

    public static RCaronException NamedArgumentNotFound(in ReadOnlySpan<char> argName)
        => new($"argument '{argName}' not found", ExceptionCode.NamedArgumentNotFound);

    public static RCaronException ArgumentsLeftUnassigned()
        => new("required arguments left unassigned", ExceptionCode.ArgumentsLeftUnassigned);

    public static RCaronException TypeNotFound(string name)
        => new($"type '{name}' not found", RCaronExceptionCode.TypeNotFound);
}