using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using PrettyPrompt;
using PrettyPrompt.Highlighting;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;

namespace RCaron.Shell.Prompt;

public class RCaronPromptCallbacks : PromptCallbacks
{
    public AnsiColor[] PairColors { get; set; } =
    {
        AnsiColor.Yellow,
        AnsiColor.Magenta,
        AnsiColor.Cyan,
        AnsiColor.BrightGreen,
    };

    public ConsoleFormat PairErrorFormat { get; set; } = new(AnsiColor.BrightRed, Underline: true);
    public bool ColorizeBracketsOutOfLimit { get; set; } = false;
    public bool HighlightBracketPairs { get; set; } = true;

    private IGrammar? Grammar { get; set; }
    private Registry? Registry { get; set; }

    [MemberNotNullWhen(true, nameof(Grammar), nameof(Registry))]
    public bool UseTextMateHighlighting { get; private set; }

    protected override Task<IReadOnlyCollection<FormatSpan>> HighlightCallbackAsync(string text,
        CancellationToken cancellationToken)
    {
        var spans = new List<FormatSpan>();
        if (UseTextMateHighlighting)
        {
            var result = Grammar.TokenizeLine(text);
            var theme = Registry.GetTheme();
            foreach (var token in result.Tokens)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var themeRules = theme.Match(token.Scopes);
                var preferred = themeRules.FirstOrDefault();
                if (preferred == null)
                    continue;
                var color = theme.GetColor(preferred.foreground);
                if (!color.StartsWith("#"))
                    continue;
                var real = ColorTranslator.FromHtml(color);
                var span = new FormatSpan(token.StartIndex, token.EndIndex - token.StartIndex,
                    AnsiColor.Rgb(real.R, real.G, real.B));
                spans.Add(span);
            }
        }

        if (HighlightBracketPairs)
        {
            var inString = false;
            var inSingleLineComment = false;
            var inMultiLineComment = false;
            short roundDepth = -1;
            short squareDepth = -1;
            short curlyDepth = -1;
            StackStack<int> roundStack = new(stackalloc int[10]);
            StackStack<int> squareStack = new(stackalloc int[10]);
            StackStack<int> curlyStack = new(stackalloc int[10]);
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '\'')
                    inString = !inString;
                else if (text[i] == '\\' && i < text.Length - 1)
                    i++;
                else if (!inString)
                {
                    if (inSingleLineComment && text[i] == '\n')
                        inSingleLineComment = false;
                    else if (inSingleLineComment)
                        continue;
                    // make sure we skip comments
                    if (i < text.Length - 1 && text[i] == '/' && text[i + 1] == '/')
                        inSingleLineComment = true;
                    else if (i < text.Length - 1 && text[i] == '/' && text[i + 1] == '#')
                        inMultiLineComment = true;
                    else if (i < text.Length - 1 && text[i] == '#' && text[i + 1] == '/')
                        inMultiLineComment = false;
                    if (inMultiLineComment)
                        continue;

                    if (text[i] == '(')
                        AddPairSpan(false, i, ref roundDepth, ref roundStack, spans);
                    else if (text[i] == ')')
                        AddPairSpan(true, i, ref roundDepth, ref roundStack, spans);
                    else if (text[i] == '[')
                        AddPairSpan(false, i, ref squareDepth, ref squareStack, spans);
                    else if (text[i] == ']')
                        AddPairSpan(true, i, ref squareDepth, ref squareStack, spans);
                    else if (text[i] == '{')
                        AddPairSpan(false, i, ref curlyDepth, ref curlyStack, spans);
                    else if (text[i] == '}')
                        AddPairSpan(true, i, ref curlyDepth, ref curlyStack, spans);
                }
            }

            AddPairErrors(ref roundStack, spans);
            AddPairErrors(ref squareStack, spans);
            AddPairErrors(ref curlyStack, spans);
        }

        return Task.FromResult((IReadOnlyCollection<FormatSpan>)spans.AsReadOnly());
    }

    void AddPairSpan(bool closing, int pos, ref short depth, ref StackStack<int> stack, List<FormatSpan> spans)
    {
        if (!closing)
        {
            depth++;
            if (depth >= 0 && !(stack.IsFull))
                stack.Push(pos);
        }

        if (ColorizeBracketsOutOfLimit && stack.IsFull && depth >= 0)
            spans.Add(new FormatSpan(pos, 1, PairColors[depth % PairColors.Length]));
        if (depth < 0)
            spans.Add(new FormatSpan(pos, 1, PairErrorFormat));
        if (closing)
        {
            if (depth >= 0 && !stack.IsEmpty)
            {
                if ((ColorizeBracketsOutOfLimit && stack.IsFull) || !stack.IsFull)
                    spans.Add(new FormatSpan(pos, 1, PairColors[depth % PairColors.Length]));
                if (!(depth > stack.Count - 1))
                    spans.Add(new FormatSpan(stack.Pop(), 1, PairColors[depth % PairColors.Length]));
            }

            depth--;
        }
    }

    void AddPairErrors(ref StackStack<int> stack, List<FormatSpan> spans)
    {
        while (!stack.IsEmpty)
        {
            var pos = stack.Pop();
            spans.Add(new FormatSpan(pos, 1, PairErrorFormat));
        }
    }

    public void UseTextMate(LocalRegistryOptions registryOptions)
    {
        Registry = new Registry(registryOptions);
        Grammar = Registry.LoadGrammar("source.rcaron");
        UseTextMateHighlighting = true;
    }

    public void DisableTextMate()
    {
        Registry = null;
        Grammar = null;
        UseTextMateHighlighting = false;
    }
}