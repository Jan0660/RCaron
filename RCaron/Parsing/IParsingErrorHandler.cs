namespace RCaron.Parsing;

public interface IParsingErrorHandler
{
    public bool Handle(ParsingException exception);
    public bool AllowsExecution();
}

public record struct TextSpan(int Position, int Length);