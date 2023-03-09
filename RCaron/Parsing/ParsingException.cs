﻿namespace RCaron.Parsing;

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

    public static ParsingException TooShortUnicodeEscape(ReadOnlySpan<char> escape, int expectedLength, TextSpan location)
        => new($"Too short unicode escape '{escape}', expected {expectedLength} characters",
            RCaronExceptionCode.TooShortUnicodeEscape, location);
}