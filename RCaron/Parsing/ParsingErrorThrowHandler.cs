namespace RCaron.Parsing;

public class ParsingErrorThrowHandler : IParsingErrorHandler
{
    public bool Threw { get; private set; }

    public bool Handle(ParsingException exception)
    {
        Threw = true;
        throw exception;
    }

    public bool AllowsExecution()
        => !Threw;
}