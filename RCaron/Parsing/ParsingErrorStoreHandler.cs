namespace RCaron.Parsing;

public class ParsingErrorStoreHandler : IParsingErrorHandler
{
    public List<ParsingException> Exceptions { get; } = new();

    public bool Handle(ParsingException exception)
    {
        Exceptions.Add(exception);
        return true;
    }

    public bool AllowsExecution()
        => true;
}