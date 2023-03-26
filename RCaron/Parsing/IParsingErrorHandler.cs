namespace RCaron.Parsing;

public interface IParsingErrorHandler
{
    public bool Handle(ParsingException exception);
    public bool AllowsExecution();
}

public record struct TextSpan(int Position, int Length)
{
    public static TextSpan FromToken(PosToken token)
        => new(token.Position.Start, token.Position.End - token.Position.Start);

    public static TextSpan FromStartAndEnd(int start, int end)
        => new(start, end - start);
}