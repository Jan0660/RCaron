using RCaron.Parsing;

namespace RCaron.LanguageServer;

public class ParsingErrorDontCareHandler : IParsingErrorHandler
{
    public bool Handle(ParsingException exception)
        => true;

    public bool AllowsExecution()
        => false;
}