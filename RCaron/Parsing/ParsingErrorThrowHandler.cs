namespace RCaron.Parsing;

public class ParsingErrorThrowHandler : IParsingErrorHandler
{
    public bool Handle(ParsingException exception)
    {
        throw exception;
    }

    public bool AllowsExecution()
        => true;
}