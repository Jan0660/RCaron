using RCaron.AutoCompletion;
using RCaron.Parsing;

namespace RCaron.Shell.Prompt;

public class ShellCompletionExtension : ICompletionExtension
{
    public Shell Shell { get; }
    
    public ShellCompletionExtension(Shell shell)
    {
        Shell = shell;
    }

    public void OnToken(List<Completion> completions, PosToken token, int caretPosition, int maxCompletions,
        LocalScope? localScope,
        IList<IRCaronModule>? modules, RCaronParserContext context, CancellationToken cancellationToken = default)
    {
        if (token is KeywordToken keywordToken)
        {
            if (completions.Count >= maxCompletions)
                return;
            foreach (var alias in Shell.ExecutableAliases)
            {
                if (alias.Key.StartsWith(keywordToken.String, StringComparison.OrdinalIgnoreCase))
                    completions.Add(new Completion(new CompletionThing()
                    {
                        Word = alias.Key,
                        Kind = CompletionItemKind.File,
                        Detail = $"(ExecAlias) {alias.Key} => {alias.Value}",
                    }, token.Position));
            }
        }
    }
}