using RCaron.Parsing;

namespace RCaron.AutoCompletion;

public interface ICompletionExtension
{
    public void OnToken(List<Completion> completions, PosToken token, int caretPosition, int maxCompletions,
        LocalScope? localScope, IList<IRCaronModule>? modules, RCaronParserContext context,
        CancellationToken cancellationToken = default);
}