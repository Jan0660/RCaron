using System.Diagnostics;
using System.Drawing;
using PrettyPrompt;
using PrettyPrompt.Highlighting;
using TextMateSharp.Grammars;
using TextMateSharp.Internal.Grammars;
using TextMateSharp.Internal.Themes.Reader;
using TextMateSharp.Internal.Types;
using TextMateSharp.Registry;
using TextMateSharp.Themes;

namespace RCaron.Shell;

public class RCaronPromptCallbacks : PromptCallbacks
{
    private IGrammar Grammar { get; }
    private Registry Registry { get; }
    public RCaronPromptCallbacks()
    {
        Registry = new Registry(new LocalRegistryOptions());
        Grammar = Registry.LoadGrammarFromPathSync(@"C:\Users\Jan\source\rcaron-vscode\syntaxes\rcaron.tmLanguage.json", 123456,
            new());
    }

    protected override Task<IReadOnlyCollection<FormatSpan>> HighlightCallbackAsync(string text,
        CancellationToken cancellationToken)
    {
        var spans = new List<FormatSpan>();
        var result = Grammar.TokenizeLine(text);
        var theme = Registry.GetTheme();
        foreach(var token in result.Tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var themeRules = theme.Match(token.Scopes);
            var preferred = themeRules.FirstOrDefault();
            if(preferred == null)
                continue;
            var color = theme.GetColor(preferred.foreground);
            if(!color.StartsWith("#"))
                continue;
            var real = ColorTranslator.FromHtml(color);
            var span = new FormatSpan(token.StartIndex, token.EndIndex - token.StartIndex, AnsiColor.Rgb(real.R, real.G, real.B));
            spans.Add(span);
        }
        return Task.FromResult((IReadOnlyCollection<FormatSpan>)spans.AsReadOnly());
    }
}

class LocalRegistryOptions : IRegistryOptions
{
    public ICollection<string> GetInjections(string scopeName)
    {
        return null;
    }

    public IRawGrammar GetGrammar(string scopeName)
    {
        return null;
    }

    public IRawTheme GetTheme(string scopeName)
    {
        switch (scopeName)
        {
            case "./dark_vs.json":
                return GetRawTheme(@"C:\Users\Jan\source\vscode\extensions\theme-defaults\themes\dark_vs.json");
            default:
                Debug.WriteLine("Unknown theme: " + scopeName);
                break;
        }
        return null;
    }

    public IRawTheme GetDefaultTheme()
        => GetRawTheme(@"C:\Users\Jan\source\vscode\extensions\theme-defaults\themes\dark_plus.json");

    public IRawTheme GetRawTheme(string themePath)
    {
        using var reader = new StreamReader(themePath);
        return ThemeReader.ReadThemeSync(reader);
    }
}