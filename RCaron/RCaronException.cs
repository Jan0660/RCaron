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
    ClassFunctionNotFound,
    ClassPropertyNotFound,
    NoSuitableIndexerImplementation,
    InvalidEscape,
    PositionalArgumentAfterNamedArgument,
    LetVariableTypeMismatch,
    ImportNotFound,
    InvalidUnicodeEscape,
    ExecutionNotAllowed,
    TooShortUnicodeEscape,
    LonelyVariableStart,
    ExpectedConstant,
    InvalidHexNumber,
    InvalidNumberSuffix,
    InvalidClassMember,
    ClassStaticFunctionNotFound,
    ClassStaticPropertyNotFound,
    StaticPropertyWithoutInitializer,
    InvalidCharacterLiteral,
    UnterminatedString,
    UnterminatedCharacterLiteral,
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

    public static RCaronException ArgumentsLeftUnassigned(in ReadOnlySpan<char> argName)
        => new($"required arguments left unassigned -> currently '{argName}'", ExceptionCode.ArgumentsLeftUnassigned);

    public static RCaronException TypeNotFound(string name)
        => new($"type '{name}' not found", RCaronExceptionCode.TypeNotFound);

    public static RCaronException PositionalArgumentAfterNamedArgument()
        => new("hit positional argument after a named one",
            RCaronExceptionCode.PositionalArgumentAfterNamedArgument);

    public static RCaronException LetVariableTypeMismatch(string name, Type type, Type valueType)
        => new($"let-variable '{name}' is of type '{type}' and cannot be assigned a value of type '{valueType}'",
            RCaronExceptionCode.LetVariableTypeMismatch);

    public static RCaronException ClassPropertyNotFound()
        => new("Class has no properties", RCaronExceptionCode.ClassPropertyNotFound);

    public static RCaronException ClassPropertyNotFound(string propertyName)
        => new($"Class property of name '{propertyName}' not found", RCaronExceptionCode.ClassPropertyNotFound);

    public static RCaronException ClassStaticPropertyNotFound(string propertyName)
        => new($"Class static property of name '{propertyName}' not found", RCaronExceptionCode.ClassStaticPropertyNotFound);

    public static RCaronException ClassFunctionNotFound(string functionName)
        => new($"Class function '{functionName}' not found",
            RCaronExceptionCode.ClassFunctionNotFound);

    public static RCaronException ClassStaticFunctionNotFound(string functionName)
        => new($"Class static function '{functionName}' not found",
            RCaronExceptionCode.ClassStaticFunctionNotFound);
    
    public static RCaronException FunctionToImportNotFound(string functionName)
        => new($"Function '{functionName}' to import not found",
            RCaronExceptionCode.ImportNotFound);
    
    public static RCaronException ClassToImportNotFound(string className)
        => new($"Class '{className}' to import not found",
            RCaronExceptionCode.ImportNotFound);
    
    public static RCaronException ExecutionNotAllowed()
        => new("Execution not allowed due to parsing errors", RCaronExceptionCode.ExecutionNotAllowed);
}