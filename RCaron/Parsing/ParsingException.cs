using System.Diagnostics;

namespace RCaron.Parsing;

public class ParsingException : RCaronException
{
    public TextSpan Location { get; }

    public ParsingException(string message, RCaronExceptionCode exceptionCode, TextSpan location) : base(message,
        exceptionCode)
    {
        Location = location;
    }

    public static ParsingException InvalidUnicodeEscape(ReadOnlySpan<char> escape, TextSpan location)
        => new($"Invalid unicode escape '{escape}'", RCaronExceptionCode.InvalidUnicodeEscape, location);

    public static ParsingException TooShortUnicodeEscape(ReadOnlySpan<char> escape, int expectedLength,
        TextSpan location)
        => new($"Too short unicode escape '{escape}', expected {expectedLength} characters",
            RCaronExceptionCode.TooShortUnicodeEscape, location);

    public static ParsingException InvalidEscapeSequence(char c, TextSpan location)
        => new($"Invalid character to escape: {c}", RCaronExceptionCode.InvalidEscape, location);

    public static ParsingException InvalidLine(int lineNumber, TextSpan location)
        => new($"invalid line at line {lineNumber}", RCaronExceptionCode.ParseInvalidLine, location);

    public static ParsingException LonelyVariableStart(TextSpan location)
        => new("Variable start with no variable name after it", RCaronExceptionCode.LonelyVariableStart, location);

    public static ParsingException ExpectedConstant(TextSpan location)
        => new("Expected constant", RCaronExceptionCode.ExpectedConstant, location);

    public static ParsingException InvalidHexNumber(TextSpan location)
        => new("Invalid hex number", RCaronExceptionCode.InvalidHexNumber, location);

    public static ParsingException InvalidNumberSuffix(TextSpan location,
        bool unsignedOnFloatingPoint = false, bool hexOnFloatingPoint = false)
    {
        string? message = null;
        if (unsignedOnFloatingPoint)
            message =
                "Invalid number suffix, the suffix 'u' or 'U' for unsigned cannot be used on floating point numbers";
        if (hexOnFloatingPoint)
            message =
                "Invalid number suffix, cannot have a floating point suffix or number and a hex prefix at the same time";
        Debug.Assert(message != null);
        return new(message, RCaronExceptionCode.InvalidNumberSuffix, location);
    }

    public static ParsingException InvalidClassMember(LineType lineType, TextSpan location)
        => new($"Invalid class member: {lineType}", RCaronExceptionCode.InvalidClassMember, location);

    public static ParsingException StaticPropertyWithoutInitializer(string propertyName, TextSpan location)
        => new($"Static property '{propertyName}' must have an initializer",
            RCaronExceptionCode.StaticPropertyWithoutInitializer, location);

    public static ParsingException InvalidCharacterLiteral(TextSpan location)
        => new("Invalid character literal", RCaronExceptionCode.InvalidCharacterLiteral, location);
}